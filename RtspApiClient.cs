using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NINA.Core.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;

namespace NINA.RtspTimelapse.Plugin {

    /// <summary>
    /// Thin client for the RTSP Timelapse app's localhost HTTP control API.
    /// Always talks to 127.0.0.1 (the server is loopback-only and rejects any
    /// non-loopback Host header), so this only works when NINA and the app run
    /// on the same PC.
    /// </summary>
    public class RtspApiClient {
        // Single shared HttpClient (recommended .NET practice). Per-call timeouts
        // are supplied via CancellationToken by callers.
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // How long WaitForFirstFrameAsync waits for the first captured frame before failing.
        private const int StartTimeoutSeconds = 60;

        private readonly string baseUrl;

        public RtspApiClient(int port) {
            baseUrl = $"http://127.0.0.1:{port}";
        }

        public string BaseUrl => baseUrl;

        /// <summary>Build a client using the port stored in the current profile's plugin options.</summary>
        public static RtspApiClient FromProfile(IProfileService profileService) {
            var settings = new PluginOptionsAccessor(profileService, PluginConstants.PluginId);
            var port = settings.GetValueInt32(PluginConstants.PortKey, PluginConstants.DefaultPort);
            return new RtspApiClient(port);
        }

        public async Task<RtspHealth> GetHealthAsync(CancellationToken token) {
            var json = await GetAsync("/health", token).ConfigureAwait(false);
            return Deserialize<RtspHealth>(json);
        }

        public async Task<RtspStatus> GetStatusAsync(CancellationToken token) {
            var json = await GetAsync("/status", token).ConfigureAwait(false);
            return Deserialize<RtspStatus>(json);
        }

        /// <summary>
        /// Wait until capture has genuinely started, i.e. the app has saved the first frame
        /// (frame_count > 0). The app flips its "capturing" flag the instant start is requested -
        /// before the camera connects - so that flag alone isn't a reliable "started" signal.
        /// Fails fast if capture stops/errors before a frame, and gives up after a timeout.
        /// </summary>
        public async Task WaitForFirstFrameAsync(CancellationToken token) {
            for (var second = 0; second < StartTimeoutSeconds; second++) {
                token.ThrowIfCancellationRequested();
                var status = await GetStatusAsync(token).ConfigureAwait(false);
                if (status.FrameCount > 0) {
                    return;
                }
                if (!status.Capturing) {
                    var detail = string.IsNullOrWhiteSpace(status.LastError) ? "" : $" ({status.LastError})";
                    throw new RtspApiException($"RTSP capture stopped before the first frame was captured{detail}.", 0);
                }
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            }
            throw new RtspApiException($"RTSP capture did not save a frame within {StartTimeoutSeconds}s.", 0);
        }

        public Task StartCaptureAsync(CancellationToken token) =>
            PostAsync("/capture/start", null, token);

        public Task StopCaptureAsync(CancellationToken token) =>
            PostAsync("/capture/stop", null, token);

        /// <summary>
        /// POST /capture/schedule: start capture (if needed) and have the app auto-stop at
        /// <paramref name="stopAt"/> (YYYYMMDD-HHMMSS), rendering the video if <paramref name="createVideo"/>.
        /// </summary>
        public Task ScheduleCaptureAsync(string stopAt, bool createVideo, CancellationToken token) {
            var body = JsonConvert.SerializeObject(new { stop_at = stopAt, create_video = createVideo });
            return PostAsync("/capture/schedule", body, token);
        }

        /// <summary>
        /// POST /video/create. Pass a `since` timestamp (YYYYMMDD-HHMMSS) to render only
        /// frames captured at/after it; null/blank renders the latest session (all frames).
        /// </summary>
        public Task CreateVideoAsync(string since, CancellationToken token) {
            string body = string.IsNullOrWhiteSpace(since)
                ? null
                : JsonConvert.SerializeObject(new { since = since.Trim() });
            return PostAsync("/video/create", body, token);
        }

        /// <summary>
        /// POST /events: record a session event, shown on the timelapse as a caption.
        /// </summary>
        /// <remarks>
        /// Never throws. Event reporting is a nice-to-have running alongside someone's imaging
        /// session - it must not be able to fail their sequence. Every outcome is swallowed:
        /// <list type="bullet">
        /// <item>409 - the app isn't capturing, so there are no frames to attach the event to.
        /// Expected whenever a sequence runs without a timelapse; silent by design.</item>
        /// <item>404 - the app predates /events. Reported once per process with the version
        /// needed, since the plugin manifest has no way to express a dependency on the app.</item>
        /// <item>anything else - logged and dropped.</item>
        /// </list>
        /// </remarks>
        /// <returns>True if the app recorded the event.</returns>
        public async Task<bool> SendEventAsync(string title, string detail, string category,
                                               DateTime? when, CancellationToken token) {
            if (string.IsNullOrWhiteSpace(title)) {
                return false;
            }

            var payload = new Dictionary<string, object> { ["title"] = title.Trim() };
            if (!string.IsNullOrWhiteSpace(detail)) { payload["detail"] = detail.Trim(); }
            if (!string.IsNullOrWhiteSpace(category)) { payload["category"] = category.Trim(); }
            if (when.HasValue) { payload["time"] = when.Value.ToString("yyyyMMdd-HHmmss"); }

            try {
                await PostAsync("/events", JsonConvert.SerializeObject(payload), token).ConfigureAwait(false);
                return true;
            } catch (OperationCanceledException) {
                return false;  // sequence is stopping; nothing to report
            } catch (RtspApiException ex) {
                if (ex.StatusCode == 409) {
                    return false;  // not capturing - the normal case, deliberately silent
                }
                if (ex.StatusCode == 404) {
                    WarnOnce(ref versionWarningIssued,
                        "Sending timelapse events needs RTSP Timelapse 3.6.0 or newer. " +
                        "Events will be skipped until the app is updated.");
                } else {
                    WarnOnce(ref sendWarningIssued, $"Could not send timelapse event: {ex.Message}");
                }
                return false;
            } catch (Exception ex) {
                WarnOnce(ref sendWarningIssued, $"Could not send timelapse event: {ex.Message}");
                return false;
            }
        }

        // A sequence can generate many events a night. If the app is old or unreachable, every one
        // of them would log the same line, so each distinct problem is reported once per process.
        private static int versionWarningIssued;
        private static int sendWarningIssued;

        private static void WarnOnce(ref int flag, string message) {
            if (Interlocked.Exchange(ref flag, 1) == 0) {
                Logger.Warning(message);
            }
        }

        private async Task<string> GetAsync(string path, CancellationToken token) {
            try {
                using (var resp = await Http.GetAsync(baseUrl + path, token).ConfigureAwait(false)) {
                    return await ReadOrThrowAsync(resp).ConfigureAwait(false);
                }
            } catch (HttpRequestException ex) {
                throw new RtspApiException(UnreachableMessage(), 0, ex);
            }
        }

        private async Task<string> PostAsync(string path, string jsonBody, CancellationToken token) {
            try {
                using (var content = jsonBody == null
                    ? null
                    : new StringContent(jsonBody, Encoding.UTF8, "application/json"))
                using (var resp = await Http.PostAsync(baseUrl + path, content, token).ConfigureAwait(false)) {
                    return await ReadOrThrowAsync(resp).ConfigureAwait(false);
                }
            } catch (HttpRequestException ex) {
                throw new RtspApiException(UnreachableMessage(), 0, ex);
            }
        }

        private static async Task<string> ReadOrThrowAsync(HttpResponseMessage resp) {
            string text = resp.Content == null ? string.Empty : await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (resp.IsSuccessStatusCode) {
                return text;
            }
            // The API returns {"error": "..."} on failure - surface that message.
            string message = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
            try {
                var err = JObject.Parse(text).Value<string>("error");
                if (!string.IsNullOrWhiteSpace(err)) {
                    message = err;
                }
            } catch {
                /* body wasn't JSON; keep the status-line message */
            }
            throw new RtspApiException(message, (int)resp.StatusCode);
        }

        // Deserialize a JSON response, turning a malformed or empty body - e.g. the configured port
        // hit a different local service, or a 200 with a "null" body - into a clear RtspApiException
        // instead of a raw JsonException / NullReferenceException leaking to the caller.
        private static T Deserialize<T>(string json) where T : class {
            T result;
            try {
                result = JsonConvert.DeserializeObject<T>(json);
            } catch (JsonException ex) {
                throw new RtspApiException(
                    "Unexpected response from the RTSP Timelapse app (not valid JSON). " +
                    "Check the port points at the app's remote control API.", 0, ex);
            }
            return result ?? throw new RtspApiException(
                "The RTSP Timelapse app returned an empty response.", 0);
        }

        private string UnreachableMessage() =>
            $"Cannot reach the RTSP Timelapse app at {baseUrl}. " +
            "Make sure it is running on this PC with the remote control API enabled (Integrations tab) and the port matches.";
    }

    /// <summary>Raised when the API returns a non-success status or is unreachable.</summary>
    public class RtspApiException : Exception {
        public int StatusCode { get; }

        public RtspApiException(string message, int statusCode, Exception inner = null)
            : base(message, inner) {
            StatusCode = statusCode;
        }
    }
}
