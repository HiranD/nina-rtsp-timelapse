# RTSP Timelapse Control — a N.I.N.A. plugin

Control the **RTSP Timelapse Capture** app from a [N.I.N.A.](https://nighttime-imaging.eu/)
(Nighttime Imaging 'N' Astronomy) sequence. Adds sequencer instructions to start/stop
timelapse capture and render videos, a trigger that reports **session events** (autofocus,
filter changes, meridian flip, target, guiding) so the app can burn them onto the timelapse
as captions, plus an imaging-tab dock panel with live status — so NINA can run an all-sky /
scenery timelapse for exactly the duration of your imaging session, annotate it with what
happened, and auto-render it afterwards.

This plugin is a thin client over the RTSP Timelapse app's **local HTTP control API**.

## Requirements

- **N.I.N.A. 3.2 or newer** (built against the NINA.Plugin 3.2 SDK).
- The **RTSP Timelapse Capture** app running on the **same Windows PC** as NINA, with
  the remote control API enabled: **Integrations tab → Enable remote control API**.
  Event reporting needs app **3.6.0 or newer** (against an older app the plugin notes it
  once in the log and everything else works as before).
- The API is **loopback-only** (`127.0.0.1`) with **no auth token** — by design it only
  works same-machine. Cross-machine use is not supported.

## What it adds

**Sequencer instructions** (category *RTSP Timelapse*):

| Instruction | API call | Notes |
|---|---|---|
| **Start Timelapse Capture** | `POST /capture/start` | Starts capture (no-op if already capturing). *Wait for capture to start* (default on) waits for the first frame; *Stop capturing if the sequence is stopped* (default off) — tick to stop capture if you abort before a Stop block (off keeps capture running through a stop/resume). |
| **Stop Timelapse Capture** | `POST /capture/stop` (+ `/video/create`) | Stops capture, then — if *Create video when finished* is ticked (default on) — renders this session's video (only its frames; delivered per the app's delivery settings). |
| **Scheduled Timelapse** | `POST /capture/schedule` | One block: starts capture and tells the **app** to auto-stop at a chosen time (Source like Nautical Dawn + offset, reusing NINA's Wait-for-Time picker) and optionally render. No Stop block needed; the app owns the timer, so it stops at the time **even if the sequence is stopped**. Non-blocking — the sequence continues immediately. |

**Sequencer trigger** (category *RTSP Timelapse*):

| Trigger | API call | Notes |
|---|---|---|
| **Report Timelapse Events** | `POST /events` | Reports what happens during the session so the app can caption the timelapse (app **3.6.0+**). It can report sequence instructions as they finish (autofocus, plate solves, dome, park/unpark, camera cool/warm), **filter changes by name** ("Filter: Ha", repeats skipped), the **meridian flip** start and finish (it watches the flip *trigger*, so built-in and DIY flips both work), **target changes** ("Target: M31", once per change) and **guiding lost/resumed** with the current RMS. Adding the trigger to a sequence is the opt-in; *which* event types are sent is chosen on the plugin's options page — one toggle per type, saved per profile, applied immediately (even mid-sequence). Target, meridian flip and centering are on by default. Event sending can never fail a sequence: with the app stopped or unreachable, sends are skipped silently. |

**Dock panel** (*RTSP Timelapse*, on the Imaging tab): live connection/version,
capturing state, frame counts, uptime and errors, with manual Start/Stop/Create-Video
buttons.

**Options** (Plugins tab → RTSP Timelapse Control): the API **Port** (default `8787`,
must match the app), a *Test connection* button, and the **Session events** selection —
one checkbox per event type the Report Timelapse Events trigger may send.

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
3. In an Advanced Sequence, add two blocks:
   - **Start Timelapse Capture** at the start (waits for the first frame by default).
   - **Stop Timelapse Capture** at the end — leave *Create video when finished* ticked to render
     this session's video (only its frames, so an earlier same-evening test capture sharing the date
     folder isn't pulled in; delivered per the app's delivery settings).

   Or use **one block**: add **Scheduled Timelapse** — it starts capture and the **app** auto-stops +
   renders at a chosen time (no Stop block). It's **non-blocking** (the sequence continues), and the
   scheduled stop fires even if you stop the sequence. The app's Stop button — or a Stop block — cancels it.

4. **Optional — captions on the video**: add the **Report Timelapse Events** trigger to the
   sequence root, pick the event types on the plugin's options page, and tick **Overlay
   session events** on the app's **Video Export** tab. Autofocus runs, filter changes,
   meridian flips, target changes and guiding loss then appear on the finished timelapse at
   the moment they happened (app 3.6.0+).

   Notes:
   - By default, stopping the NINA sequence does **not** stop capture, so a stop and resume keeps the
     timelapse running (the Stop block ends it). **Tick** *Stop capturing if the sequence is stopped*
     on the Start block if you'd rather capture always stop when the sequence stops.
   - A restart from the beginning begins a fresh session.

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
SessionEvents.cs                   event-type catalog + options-page toggle view models
Instructions/                      StartCapture, StopCapture, CreateVideo + templates
Triggers/                          ReportTimelapseEvents trigger + templates
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
