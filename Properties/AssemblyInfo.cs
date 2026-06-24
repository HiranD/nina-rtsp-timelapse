using System.Reflection;
using System.Runtime.InteropServices;

// [MANDATORY] The unique identifier of this plugin. NEVER change it once published.
[assembly: Guid("b3f2c1a4-7d6e-4f9a-9c2b-1e5d8a0f3c47")]

// [MANDATORY] Plugin name shown in the Plugins tab.
[assembly: AssemblyTitle("RTSP Timelapse Control")]

// [MANDATORY] Version: Major.Minor.Patch.Build
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

// [RECOMMENDED] Author / company.
[assembly: AssemblyCompany("HiranD")]
[assembly: AssemblyProduct("RTSP Timelapse Control")]
[assembly: AssemblyCopyright("Copyright © 2026 HiranD")]
[assembly: AssemblyDescription("Control the RTSP Timelapse Capture app from a N.I.N.A. sequence.")]

// [MANDATORY] Short description shown in the plugin list.
[assembly: AssemblyMetadata("ShortDescription",
    "Start/stop RTSP timelapse capture and render videos from your N.I.N.A. sequence — the app can auto-upload each finished timelapse to Discord.")]

// [OPTIONAL] Long (markdown) description shown on the plugin detail page.
[assembly: AssemblyMetadata("LongDescription",
    "**RTSP Timelapse Capture** is a free, open-source (MIT) Windows app that turns any "
    + "RTSP / IP camera — security cams, all-sky cameras, webcams — into a hands-off timelapse "
    + "studio. This plugin lets N.I.N.A. drive it as part of your imaging sequence.\n\n"
    + "### What the app does\n"
    + "- Capture stills from any RTSP stream on a set interval, with rock-solid reconnection "
    + "(±5s timestamp accuracy, 100% capture success rate).\n"
    + "- Smart overnight scheduling with twilight calculations (civil / nautical / astronomical "
    + "darkness) or manual times, planned across a calendar.\n"
    + "- One-click MP4 export with presets — frame rate, speed-up, CRF quality and resolution.\n"
    + "- Auto-create the video after each night's session and auto-post it to a Discord channel.\n"
    + "- Minimize-to-tray and start-with-Windows for unattended, headless rigs.\n\n"
    + "### What this plugin adds\n"
    + "Sequencer instructions to **Start Capture**, **Stop Capture** and **Create Timelapse Video**, "
    + "plus an imaging-tab dock panel showing live capture status. Let N.I.N.A. run your scenery or "
    + "all-sky timelapse for exactly the duration of your imaging session, then render it automatically. "
    + "Create Timelapse Video honours the app's Discord settings, so the finished clip can post straight "
    + "to your Discord channel.\n\n"
    + "### Get the app\n"
    + "Download / docs: **https://github.com/HiranD/RTSP-Timelapse-Capture**\n\n"
    + "Requires the RTSP Timelapse app running on the **same PC** with the remote control API enabled "
    + "(Integrations tab). The connection is loopback-only (127.0.0.1), so NINA and the app must share one machine.")]

// [RECOMMENDED] Licensing + links.
[assembly: AssemblyMetadata("License", "MIT")]
[assembly: AssemblyMetadata("LicenseURL", "https://opensource.org/licenses/MIT")]
[assembly: AssemblyMetadata("Repository", "https://github.com/HiranD/nina-rtsp-timelapse")]
// Homepage points at the APP so the Plugins-tab link helps users discover RTSP Timelapse Capture.
[assembly: AssemblyMetadata("Homepage", "https://github.com/HiranD/RTSP-Timelapse-Capture")]
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/HiranD/nina-rtsp-timelapse/blob/main/CHANGELOG.md")]
[assembly: AssemblyMetadata("Tags", "Timelapse,RTSP,Camera,Automation,AllSky")]

// [OPTIONAL] Featured image on the plugin page = the app's official icon (hosted in the app repo).
[assembly: AssemblyMetadata("FeaturedImageURL", "https://raw.githubusercontent.com/HiranD/RTSP-Timelapse-Capture/main/assets/icon.png")]

// [RECOMMENDED] Earliest N.I.N.A. version this plugin is compatible with.
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.0.0.0")]

[assembly: ComVisible(false)]
