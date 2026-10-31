# Windows USB setup

The MTK suite supports Windows x64 only in this deliverable.

## Driver

Use Zadig for the exact BROM/Preloader interface:

- **WinUSB:** recommended default.
- **libusbK:** supported alternative.
- **UsbDk:** explicitly unsupported and must not be installed.

Zadig should be run with the device connected in the required mode. A phone
can re-enumerate after BROM, Preloader or DA transitions, so verify the
driver again for the new interface rather than assuming a previous VID/PID
remains valid.

Do not install a generic driver over unrelated ADB, Fastboot or composite
interfaces. Record the VID/PID and interface selected in the device manifest.

## Native runtime

`LibUsbDotNet` loads `libusb-1.0.dll`. Supply the official x64 DLL beside the
application executable, or build with:

```powershell
$env:LIBUSB_ROOT = 'C:\Tools\libusb-1.0.29'
dotnet build .\TensaZangetsu.Mtk.slnx -c Release
dotnet publish .\src\TensaZangetsu.Mtk.Cli -c Release -r win-x64 --self-contained false
```

The project does not install drivers, services or filters. It does not use or
probe UsbDk.

## COM/VCOM fallback

Some MediaTek devices expose BROM through a Windows `COMn` port instead of a
native LibUsb interface. The shared MTK backend accepts an explicitly selected
COM port and performs only the bounded initial BROM identification exchange.
This route does not require the native DLL and does not upload DA/EMI/Auth or
write device storage.
