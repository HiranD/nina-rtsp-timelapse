using System;

namespace NINA.RtspTimelapse.Plugin {

    /// <summary>Shared constants for the plugin.</summary>
    public static class PluginConstants {

        /// <summary>
        /// Must match the <c>[assembly: Guid(...)]</c> in Properties/AssemblyInfo.cs.
        /// Used to key the per-profile plugin options so instructions and the
        /// dock panel read the same stored Port as the options page.
        /// </summary>
        public static readonly Guid PluginId = Guid.Parse("b3f2c1a4-7d6e-4f9a-9c2b-1e5d8a0f3c47");

        /// <summary>Persisted-option key for the API port.</summary>
        public const string PortKey = "Port";

        /// <summary>Default port (matches the RTSP Timelapse app's Integrations tab default).</summary>
        public const int DefaultPort = 8787;
    }
}
