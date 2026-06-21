using System;

namespace NINA.RtspTimelapse.Plugin {

    /// <summary>
    /// Process-global state for the capture session the plugin most recently started.
    /// Only one capture can run at a time (the app's remote API and its own scheduler
    /// are mutually exclusive), so a single shared value is sufficient.
    /// </summary>
    public static class RtspSession {

        /// <summary>
        /// The "since" timestamp (YYYYMMDD-HHMMSS) of the last Start issued by the plugin.
        /// Passed to /video/create so the render includes only this session's frames, even
        /// when earlier frames (e.g. a test run) share the same date folder. Null until the
        /// plugin starts a capture.
        /// </summary>
        public static string LastStartSince { get; set; }

        /// <summary>
        /// Format a capture-start instant as the API's `since` timestamp, with a small
        /// safety margin so the session's first frame is never filtered out.
        /// </summary>
        public static string FormatSince(DateTime now) =>
            now.AddSeconds(-2).ToString("yyyyMMdd-HHmmss");
    }
}
