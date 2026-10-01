# Third-party notices

TonyTouchBar is distributed as an aggregate of separately licensed components.

## Windows host

- [.NET](https://github.com/dotnet/runtime), MIT license.
- [SkiaSharp](https://github.com/mono/SkiaSharp), MIT license.
- Windows SDK metadata/runtime APIs, subject to Microsoft's applicable license terms.

The Windows host source in `src/T2TouchBar` is licensed under the repository MIT license.

## Linux userspace bridge

The source in `bridge/` is licensed GPL-2.0-only. Its Apple Display Framebuffer protocol implementation was derived from the Linux [`appletbdrm`](https://linux.googlesource.com/linux/kernel/git/torvalds/linux/+/master/drivers/gpu/drm/tiny/appletbdrm.c) driver behavior. Protocol credit belongs to Kerem Karabay and the Linux `appletbdrm` contributors. It dynamically links to [libusb](https://github.com/libusb/libusb), LGPL-2.1-or-later.

The bridge and Windows host communicate only through standard input/output and are distributed as separate executables in the release archive.

## Explicit exclusions

This repository and its releases do **not** contain Apple Boot Camp drivers, certificates, private keys, modified Windows kernel drivers, UsbDk, libusbK, or code from the unlicensed `FelipeNicoletto/WindowsTouchBar` repository.
