# Security policy

## Supported versions

Only the latest GitHub release receives security fixes while TonyTouchBar is in preview.

## Reporting a vulnerability

Please use GitHub's private vulnerability reporting feature instead of opening a public issue. Include the affected version and minimal reproduction details. Do not attach logs containing information you have not reviewed.

TonyTouchBar does not send telemetry and does not require network access after prerequisites are installed. The installer requests elevation only for the one-time `usbipd bind` operation. The regular Windows host runs as the signed-in user.
