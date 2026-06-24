# Changelog

## 1.2.0

- **Redesign:** removed the **Auto Timelapse** single block (it relied on NINA's run lifecycle and
  couldn't survive stopping and resuming from the middle of a sequence) and the standalone **Create
  Timelapse Video** block. The plugin is now two explicit blocks:
  - **Start Timelapse Capture** — starts capture; waits for the first frame by default;
    *Stop capturing if the sequence is stopped* (default on) so capture isn't left running on an abort
    (untick to keep capture running through a stop/resume).
  - **Stop Timelapse Capture** — stops capture, then optionally renders this session's video
    (*Create the video after stopping*, default on).
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
