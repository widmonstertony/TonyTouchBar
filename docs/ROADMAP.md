# TonyTouchBar product roadmap

TonyTouchBar is designed as a small context surface, not a row of permanent buttons. The foreground task decides what matters now; controls remain glanceable and do not cover the main display.

## v0.2 — scene engine

- One-file consumer installer with dependency checks, automatic resume after a required WSL restart, repair, and uninstall paths.
- Codex Live states from official lifecycle hooks: thinking, tool activity, permission required, finished, and interrupted.
- Windows Photos controls and a configurable app-profile model.
- Refined game, media, and default layouts.

## v0.3 — games that feel alive

- ✅ Forza Data Out receiver for gear, RPM, speed, lap and rev lights. Telemetry stays local.
- ✅ Race layout: game icon/title on the left, gear and RPM in the center, lap/position and capture controls on the right.
- Photo-mode layout: shutter, hide UI, camera movement presets, exposure/depth-of-field adjustments, and the most recent capture preview where the game exposes reliable controls.
- Generic game overlay status for capture/recording, battery, temperature and frame pacing, subject to stable public Windows APIs and third-party licensing.

## v0.3 — richer video

- Smooth volume slider and media-device selector.
- Optional browser companion for chapter markers, playback speed, subtitles, fullscreen and picture-in-picture on YouTube and Bilibili.
- Keep the basic timeline working without a browser extension through Windows media sessions.

## v0.4 — photo and creator workflows

- Latest-screenshot thumbnail with open, copy and “send to Codex” actions.
- Lightroom and Photoshop profiles for before/after, crop, zoom, brush size, undo/redo and rating.
- Configurable creator controls rather than fragile app automation; deeper integrations only where an application offers a stable API.

## Design rules

- Secure Boot stays enabled.
- No cloud account or telemetry is required.
- No fake state: “Codex is working” comes from a lifecycle event; media progress comes from the media session; game telemetry comes from the game.
- Important controls must remain usable when optional browser, Codex or telemetry integrations are absent.
- Never auto-approve a Codex permission request from the Touch Bar. The strip may alert and focus Codex, but the user reviews the action in Codex.
