# Tensa-Zangetsu MTK Suite

Standalone GUI and CLI for authorized MediaTek servicing. The suite uses the
repository MTK protocol and transport implementations; it does not fabricate
device information and does not implement authentication bypasses or exploits.

## Windows build

The supported target is **Windows x64**. UsbDk is not used or required.
The USB stack is `LibUsbDotNet` over native `libusb-1.0.dll`.

Install the device driver with [Zadig](https://zadig.akeo.ie/) for the
specific MediaTek USB interface:

1. Enable **Options > List All Devices**.
2. Select the connected BROM/Preloader interface, checking VID/PID carefully.
3. Install **WinUSB** (recommended) or **libusbK**.
4. Do not select or install UsbDk, and do not replace unrelated Android
   interfaces.

Install the official 64-bit `libusb-1.0.dll` beside the published executable,
or set `LIBUSB_ROOT` before building so the project copies it automatically:

```powershell
$env:LIBUSB_ROOT = 'C:\Tools\libusb-1.0.29'
dotnet build .\TensaZangetsu.Mtk.slnx -c Release
dotnet publish .\src\TensaZangetsu.Mtk.Cli -c Release -r win-x64 --self-contained false
```

The native DLL must be x64 and must be named `libusb-1.0.dll`; no UsbDk
service, filter or runtime is probed or installed.

```bash
dotnet build TensaZangetsu.Mtk.slnx -c Release
```

## Identify a real device

```bash
dotnet run --project src/TensaZangetsu.Mtk.Cli -- identify
dotnet run --project src/TensaZangetsu.Mtk.Cli -- identify --json
```

The device must be connected in an authorized MediaTek BROM/Preloader mode.
Windows COM/VCOM candidates use the serial transport and do not require the
native LibUsb runtime. Native USB enumeration is deliberately disabled by
default after startup; enable it only after supplying a verified x64 runtime:

```powershell
$env:TENSAZANGETSU_ENABLE_NATIVE_USB_SCAN = '1'
dotnet run --project .\src\TensaZangetsu.Mtk.Cli -- identify
```

A missing DLL, driver or device is reported as an error; no fallback data is
fabricated. Identification does not upload a DA, authenticate, write storage
or execute a bypass.

## Device asset layout

Resources are added manually per device under:

```text
Assets/devices/<device-id>/
  device.json
  loaders/DA.bin
  loaders/preloader.bin
  loaders/emi.bin
  firmware/scatter.txt
  auth/certificate.bin
  auth/signature-provider.json
```

The binaries are intentionally not included. `device.json` should contain
hashes, provenance and compatibility constraints, not guessed model mappings.
Existing resources can remain outside this tree and be referenced by a future
manifest without copying them.

## Import existing Windows assets

From Windows PowerShell, import the complete `D:\dbge\Documents` tree:

```powershell
.\scripts\import-mtk-assets.ps1 -Source 'D:\dbge\Documents'
```

The script searches every subfolder, hashes files, copies identified MTK
assets into model inventory folders and creates `Catalog\mtk-assets.json`.
Unknown files are preserved under `inventory`. Directory names are only
inventory hints; compatibility must be reviewed before selecting a loader.

List the imported model groups:

```powershell
dotnet run --project .\src\TensaZangetsu.Mtk.Cli -- catalog
dotnet run --project .\src\TensaZangetsu.Mtk.Cli -- catalog --json
```
