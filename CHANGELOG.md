# Changelog

## Unreleased

## 0.3.0-preview.2 - 2026-10-02

- Fixed stale unfinished or background Codex turns keeping Codex Live in the working state after the foreground turn completed.

## 0.3.0-preview.1 - 2026-10-02

- Added reliable Codex Live detection from Codex turn history, with exact working and completed states instead of filesystem activity guesses.
- Fixed Codex Live getting stuck on working after a turn had already completed.
- Fixed Bilibili desktop playback controls when the app publishes media actions but no timeline; the -10s/+10s buttons now use its registered rewind/fast-forward actions.
- Improved media-session selection so the foreground player wins when several browsers or media apps are open.
- Added the first Forza racing dashboard for FH5, FH6, and Forza Motorsport.
- Shows live gear, speed, RPM shift lights, lap/position, screenshot, and Photo Mode controls from the local Data Out stream.
- Auto-detects Motorsport and Horizon Car Dash packet layouts and returns to the normal scene when Forza loses focus.
- Added a configurable `forzaTelemetryPort` setting, defaulting to UDP `5607`.
- Added a native Fn overlay with brightness, F1–F10, and volume controls through Apple's Boot Camp KeyManager.
- Replaced the Forza `DATA OUT` warning with a clock, battery, animated game-mode strip, and ready state.

## 0.2.0-preview.2 - 2026-10-01

- Fixed a black Touch Bar after Windows lock/unlock, sleep, or a USB reset.
- Added automatic USB/IP reattachment and a bounded two-stage bridge recovery path.
- Detects and clears stale WSL bridge processes, including the rare uninterruptible USB state.
- Replaced the startup binary handshake with a lock-safe line handshake while keeping binary frame acknowledgements.
- Reuses a healthy existing attachment for faster normal startup.

## 0.2.0-preview.1 - 2026-10-01

- Added a one-file graphical installer that automates WSL, Ubuntu, usbipd-win, device sharing, the Linux bridge, startup registration, and optional Codex integration.
- Added automatic Setup resume when Windows requires a restart to enable WSL.
- Added Codex Live states driven by official lifecycle hooks: thinking, tool activity, approval, finished, and interrupted.
- Added Windows Photos profiles and modifier-key shortcuts.
- Added a scene-focused product roadmap for game telemetry, richer browser media, and creator workflows.
- Release automation now publishes `TonyTouchBar-Setup.exe`, the portable ZIP, and release-asset checksums.
- Added a standard Windows Apps uninstall entry while preserving user settings by default.

## 0.1.0-preview.1 - 2026-10-01

- First public preview of TonyTouchBar by Tony.
- Secure-Boot-compatible WSL/libusb display and touch bridge.
- Foreground app icon/title layout and configurable game profiles.
- Battery/charging status and media/volume controls.
- YouTube and Bilibili now-playing title, playback controls, and seek bar through Windows GSMTC.
- Per-user installer, startup registration, uninstaller, and diagnostics bundle.
- Self-contained, reproducible preview archive with SHA-256 checksums.
