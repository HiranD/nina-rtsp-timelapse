# Changelog

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
