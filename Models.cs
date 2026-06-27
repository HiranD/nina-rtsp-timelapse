using Newtonsoft.Json;

namespace NINA.RtspTimelapse.Plugin {

    /// <summary>Response of <c>GET /health</c>.</summary>
    public class RtspHealth {
        [JsonProperty("ok")] public bool Ok { get; set; }
        [JsonProperty("version")] public string Version { get; set; }
    }

    /// <summary>Response of <c>GET /status</c> (also embedded in start/stop responses).</summary>
    public class RtspStatus {
        [JsonProperty("capturing")] public bool Capturing { get; set; }
        [JsonProperty("state")] public string State { get; set; }
        [JsonProperty("frame_count")] public int FrameCount { get; set; }
        [JsonProperty("failed_frame_count")] public int FailedFrameCount { get; set; }
        [JsonProperty("uptime_seconds")] public int UptimeSeconds { get; set; }
        [JsonProperty("last_error")] public string LastError { get; set; }
        [JsonProperty("scheduler_enabled")] public bool SchedulerEnabled { get; set; }
        // The app-owned start of the current/most-recent session (YYYYMMDD-HHMMSS), or null after
        // an app restart. The single source of truth for which session a render should cover.
        [JsonProperty("session_start_time")] public string SessionStart { get; set; }
    }
}
