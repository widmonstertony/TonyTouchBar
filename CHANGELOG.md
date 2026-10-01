# Changelog

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
