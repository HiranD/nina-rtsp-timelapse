using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;

namespace NINA.RtspTimelapse.Plugin.Instructions {

    [ExportMetadata("Name", "Start Timelapse Capture")]
    [ExportMetadata("Description", "Starts RTSP timelapse capture via the app's local HTTP API. No-op if already capturing.")]
    [ExportMetadata("Icon", "RtspTimelapse_SVG")]
    [ExportMetadata("Category", "RTSP Timelapse")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class StartCapture : SequenceItem {
        private readonly IProfileService profileService;

        [ImportingConstructor]
        public StartCapture(IProfileService profileService) {
            this.profileService = profileService;
        }

        private StartCapture(StartCapture copyMe) : this(copyMe.profileService) {
            CopyMetaData(copyMe);
            WaitUntilCapturing = copyMe.WaitUntilCapturing;
            StopWhenSequenceStops = copyMe.StopWhenSequenceStops;
        }

        private bool waitUntilCapturing = true;

        /// <summary>When true, wait until the app captures the first frame before completing.</summary>
        [JsonProperty]
        public bool WaitUntilCapturing {
            get => waitUntilCapturing;
            set { waitUntilCapturing = value; RaisePropertyChanged(); }
        }

        private bool stopWhenSequenceStops = false;

        /// <summary>
        /// When true, stop capture in Teardown (when the sequence ends or is stopped) if it's still
        /// running - a safety net so capture isn't left running if the sequence is aborted before a
        /// Stop block. Off by default so a stop/resume keeps capture running, and to encourage pairing
        /// with an explicit Stop block; turn on to stop capture whenever the sequence stops.
        /// </summary>
        [JsonProperty]
        public bool StopWhenSequenceStops {
            get => stopWhenSequenceStops;
            set { stopWhenSequenceStops = value; RaisePropertyChanged(); }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var client = RtspApiClient.FromProfile(profileService);

            var status = await client.GetStatusAsync(token);
            if (status.Capturing) {
                Logger.Info("RTSP Timelapse is already capturing; Start Timelapse Capture is a no-op.");
                return;
            }

            await client.StartCaptureAsync(token);

            if (WaitUntilCapturing) {
                await client.WaitForFirstFrameAsync(token);
            }
        }

        // N.I.N.A. calls Teardown() once at the end of the run (including on a user stop/abort).
        // Optionally stop the capture, so it isn't left running if the sequence was stopped before a
        // Stop block ran. Idempotent + exception-safe (must never throw).
        //
        // NINA's Teardown is synchronous void and is NOT awaited, so we run the stop synchronously and
        // bounded here rather than as `async void` (which would return immediately and let the stop be
        // cut off when NINA is shutting down). The HTTP client awaits with ConfigureAwait(false)
        // throughout, so blocking here cannot deadlock even if this runs on the UI thread. The 10s cap
        // keeps a user "Stop sequence" responsive if the app is unreachable.
        public override void Teardown() {
            base.Teardown();
            if (!StopWhenSequenceStops) {
                return;
            }
            try {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10))) {
                    // GetAwaiter().GetResult() rethrows the inner exception directly (unlike .Wait(),
                    // which wraps it in AggregateException).
                    StopIfRunningAsync(cts.Token).GetAwaiter().GetResult();
                }
            } catch (Exception ex) {
                Logger.Error($"Start Timelapse Capture teardown failed: {ex.Message}");
            }
        }

        // Stop capture if it's currently running. Factored out of Teardown so it can run bounded.
        private async Task StopIfRunningAsync(CancellationToken token) {
            var client = RtspApiClient.FromProfile(profileService);
            var status = await client.GetStatusAsync(token).ConfigureAwait(false);
            if (status.Capturing) {
                Logger.Info("Sequence stopped - stopping RTSP capture (Stop capturing if the sequence is stopped).");
                await client.StopCaptureAsync(token).ConfigureAwait(false);
            }
        }

        public override object Clone() => new StartCapture(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(StartCapture)}";
    }
}
