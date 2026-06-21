# RTSP Timelapse Control — a N.I.N.A. plugin

Control the **RTSP Timelapse Capture** app from a [N.I.N.A.](https://nighttime-imaging.eu/)
(Nighttime Imaging 'N' Astronomy) sequence. Adds sequencer instructions to start/stop
timelapse capture and render videos, plus an imaging-tab dock panel with live status —
so NINA can run an all-sky / scenery timelapse for exactly the duration of your imaging
session and auto-render it afterwards.

This plugin is a thin client over the RTSP Timelapse app's **local HTTP control API**.

## Requirements

- **N.I.N.A. 3.0 or newer** (built and tested against the NINA.Plugin 3.2 SDK).
- The **RTSP Timelapse Capture** app running on the **same Windows PC** as NINA, with
  the remote control API enabled: **Integrations tab → Enable remote control API**.
- The API is **loopback-only** (`127.0.0.1`) with **no auth token** — by design it only
  works same-machine. Cross-machine use is not supported.

## What it adds

**Sequencer instructions** (category *RTSP Timelapse*):

| Instruction | API call | Notes |
|---|---|---|
| Start Timelapse Capture | `POST /capture/start` | No-op if already capturing. Optional *Wait until capturing*. |
| Stop Timelapse Capture | `POST /capture/stop` | No-op if not capturing. |
| Create Timelapse Video | `POST /video/create` | Optional *Date* (`YYYYMMDD`); blank = newest session. Fire-and-forget. |

**Dock panel** (*RTSP Timelapse*, on the Imaging tab): live connection/version,
capturing state, frame counts, uptime and errors, with manual Start/Stop/Create-Video
buttons.

**Options** (Plugins tab → RTSP Timelapse Control): the API **Port** (default `8787`,
must match the app) and a *Test connection* button.

## Build

You need **Visual Studio 2022** (or the **.NET 8 SDK**). The NINA SDK packages restore
from nuget.org — no VSIX template required.

```sh
dotnet restore
dotnet build -c Release
```

The output is `bin/Release/net8.0-windows7.0/NINA.RtspTimelapse.Plugin.dll`.

A **Debug** build also copies the DLL into `%LOCALAPPDATA%\NINA\Plugins\3.0.0\NINA.RtspTimelapse.Plugin\`
automatically (see the `CopyToNinaPlugins` target in the csproj) for quick iteration.

## Install (manual)

1. Copy `NINA.RtspTimelapse.Plugin.dll` into
   `%LOCALAPPDATA%\NINA\Plugins\3.0.0\NINA.RtspTimelapse.Plugin\`.
   (N.I.N.A. 3.x loads plugins from the version-bracketed `3.0.0` subfolder, **not** from `Plugins\` directly.)
2. Restart N.I.N.A. It should appear under the **Plugins** tab.

## Use

1. In the RTSP Timelapse app, enable the remote API (Integrations tab). Note the port.
2. In NINA, set the same port in the plugin's options and click *Test connection*.
3. In an Advanced Sequence, add the instructions, e.g.:
   - **Start Timelapse Capture** at session start
   - **Stop Timelapse Capture** at session end
   - **Create Timelapse Video** afterwards (blank date = the session that just ended)

## Publish to NINA's in-app Plugins tab

1. Tag a release; build the Release DLL.
2. Generate `manifest.json` from the DLL with `CreateManifest.ps1` from the
   [nina.plugin.manifests](https://github.com/isbeorn/nina.plugin.manifests) repo (or
   copy its GitHub Actions workflow from `./tools`). **Do not recompile after
   generating** — the checksum would change.
3. Fork that repo, place the file at
   `manifests/<first-letter>/<plugin name>/<nina version>/<plugin version>/manifest.json`,
   validate with `node gather.js`, and open a PR.

## Project layout

```
NINA.RtspTimelapse.Plugin.csproj   .NET 8 class library, NINA 3.2 package refs
Properties/AssemblyInfo.cs         plugin metadata + GUID (do not change the GUID)
PluginConstants.cs                 shared GUID + option keys
Models.cs                          /health + /status DTOs
RtspApiClient.cs                   HttpClient wrapper + error mapping
RtspTimelapsePlugin.cs             plugin entry point + options VM
Options.xaml(.cs)                  options page DataTemplate
Instructions/                      StartCapture, StopCapture, CreateVideo + templates
Dockable/                          status panel VM + template
```

## Compatibility note

Built against **NINA.Plugin 3.2.0.9001**. If you target a different NINA version and the
build complains, the few version-sensitive spots are: the `AsyncCommand<bool>` namespace
(`NINA.Core.Utility`), the `SequenceBlockView` XAML namespace
(`clr-namespace:NINA.View.Sequencer;assembly=NINA.Sequencer`), and the `DockableVM` /
`PluginOptionsAccessor` namespaces. Visual Studio IntelliSense will point to the right
using-statements for your installed package version.

## License

MIT — see [LICENSE](LICENSE).
