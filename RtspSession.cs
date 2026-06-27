using System;
using System.Globalization;

namespace NINA.RtspTimelapse.Plugin {

    /// <summary>
    /// Stateless helpers for turning a capture-session start instant into the remote API's
    /// <c>since</c> timestamp. The app owns "when did the current session start" (it reports
    /// <c>session_start_time</c> on /status), so the plugin keeps no session state of its own - it
    /// just formats the value it reads back.
    /// </summary>
    public static class RtspSession {

        /// <summary>
        /// Format a capture-start instant as the API's <c>since</c> timestamp (YYYYMMDD-HHMMSS),
        /// with a small safety margin so the session's first frame is never filtered out.
        /// </summary>
        public static string FormatSince(DateTime start) =>
            start.AddSeconds(-2).ToString("yyyyMMdd-HHmmss");

        /// <summary>
        /// Turn the app's reported <c>session_start_time</c> (YYYYMMDD-HHMMSS, or null) into a
        /// <c>since</c> value with the safety margin applied. Returns null when the app reports no
        /// session start (e.g. just after an app restart), so the render falls back to the whole
        /// newest folder rather than using a wrong instant.
        /// </summary>
        public static string MarginedSince(string sessionStart) {
            if (string.IsNullOrWhiteSpace(sessionStart)) {
                return null;
            }
            return DateTime.TryParseExact(sessionStart, "yyyyMMdd-HHmmss",
                       CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                ? FormatSince(start)
                : null;
        }
    }
}
