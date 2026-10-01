# Contributing

Thanks for helping improve TonyTouchBar.

1. Open an issue before large behavioral or protocol changes.
2. Keep the Windows host and GPL bridge license boundary intact.
3. Never commit Apple drivers, certificates, signing keys, crash dumps, or personal paths.
4. Run `dotnet build src/T2TouchBar/T2TouchBar.csproj -c Release` and `make -C bridge` before submitting a pull request.
5. For a new Mac model, include the model identifier, Touch Bar USB VID:PID, WSL version, usbipd-win version, and a reviewed diagnostics archive.

By contributing, you agree that Windows-host contributions are MIT licensed and bridge contributions are GPL-2.0-only.
