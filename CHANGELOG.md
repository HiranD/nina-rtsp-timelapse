# Changelog

## 1.5.0

- **New: Report Timelapse Events trigger.** Add it to a sequence and the plugin tells the RTSP
  Timelapse app what's happening during the night, so the app can burn the events onto the finished
  timelapse as captions at the moment they occurred. Adding the trigger is the on/off switch —
  there is nothing else to enable — and it reports:
  - **Sequence instructions** as they finish: autofocus, filter changes, meridian flips, plate
    solves, dithers, park/unpark. Routine exposures are deliberately ignored, otherwise a night's
    captions would bury the video.
  - **Target changes**, e.g. "Target: M31" — sent once when the target actually changes, not once
    per instruction.
  - **Guiding lost / resumed**, with the current RMS error (off by default).
- Event sending can never fail a sequence. When the app isn't capturing there are no frames to
  attach events to, so those sends are skipped silently; an unreachable app or any other error is
  logged once and dropped.
- Requires **RTSP Timelapse Capture 3.6.0 or newer** for the events feature. Against an older app
  the plugin says so once in the log and carries on — nothing else in the plugin is affected.

## 1.4.1

- **Fixed: Scheduled Timelapse could fail with "stop_at is in the past."** When armed in the
  afternoon/evening for a morning target (e.g. Until Sunrise / Dawn), the block computed a stop
  time in the past and the app rejected it. It now schedules the next future occurrence of the
  chosen time, and reports a clear error if the source has no solution (e.g. no twilight at high
  latitude in summer).
- **More reliable "Stop capturing if the sequence is stopped."** The teardown stop now runs to
  completion instead of being cut off, so capture isn't left running when a sequence is aborted.
- **Sturdier status dock.** The panel no longer stalls if the app becomes unresponsive (each poll
  is time-bounded and overlapping refreshes can't pile up).
- **Stop + Create Video renders exactly this session.** It uses the session start the app reports
  on `/status`, so the correct frames are rendered even when capture was started outside the plugin
  (the app's Stop/Start buttons or scheduler). Requires the RTSP Timelapse app build that reports
  `session_start_time`; older app builds fall back to rendering the whole newest folder.
- **Clearer connection errors** when the configured port isn't the app's remote API — an unexpected
  or empty response now explains the likely port mismatch instead of a raw error.
- Corrected the Scheduled Timelapse note on the options page (it's non-blocking).
- Requires **N.I.N.A. 3.2** or newer (matches the SDK the plugin is built against).

## 1.4.0

- **Scheduled Timelapse is now app-driven and robust:** the plugin hands the target time to the app via
  a new `POST /capture/schedule`, and the **app owns the stop timer** — so capture stops (and renders) at
  the scheduled time **even if the NINA sequence is stopped** or NINA is closed. The block is now
  **non-blocking** (the sequence continues immediately; no Parallel set needed).
- Added a **"Wait for capture to start"** toggle to Scheduled Timelapse (default on).
- Any manual stop — the app's Stop button, a Stop block, or `/capture/stop` — cancels a pending
  scheduled stop. (Requires the RTSP Timelapse app build with `/capture/schedule` support.)

## 1.3.0

- **New: Scheduled Timelapse** instruction — starts capture and runs **until a chosen time** (a Source
  like Nautical Dawn + offset, reusing NINA's "Wait for Time" picker with real twilight math), then
  stops and optionally renders. No Stop block needed. It blocks the sequence, so use it in a Parallel
  instruction set to capture alongside imaging.
- **Options page:** added a "Suggested usage" guide (Start + Stop vs Scheduled Timelapse).

## 1.2.0

- **Redesign:** removed the **Auto Timelapse** single block (it relied on NINA's run lifecycle and
  couldn't survive stopping and resuming from the middle of a sequence) and the standalone **Create
  Timelapse Video** block. The plugin is now two explicit blocks:
  - **Start Timelapse Capture** — starts capture; waits for the first frame by default;
    *Stop capturing if the sequence is stopped* (default on) so capture isn't left running on an abort
    (untick to keep capture running through a stop/resume).
  - **Stop Timelapse Capture** — stops capture, then optionally renders this session's video
    (*Create video when finished*, default on).
- By default a NINA sequence stop leaves capture running, so a stop and resume keeps the timelapse
  going; the render still includes only this session's frames (via the `since` timestamp).

## 1.1.0

- **New: Auto Timelapse** instruction — a single block at the start of a sequence that starts
  capture and automatically stops it (and optionally renders the video) when the sequence ends,
  including on abort.
- The rendered video now contains **only the frames from the session that was started** (the
  plugin sends a `since` timestamp), so an earlier same-evening test capture sharing the date
  folder isn't included. Requires the RTSP Timelapse app build with `since` support.
- Create Timelapse Video renders the session started earlier in the sequence (else the latest).

## 1.0.0

Initial release.

- Sequencer instructions: **Start Timelapse Capture**, **Stop Timelapse Capture**, **Create Timelapse Video**
- Imaging-tab dock panel showing live capture status with Start/Stop/Create-Video buttons
- Configurable API port (Options page) with a "Test connection" button
- Targets N.I.N.A. 3.x (NINA.Plugin 3.2)
