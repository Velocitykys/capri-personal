# Capri Mobile (Android)

This is a separate Android app project. It does not connect to the Windows Capri installation and does not share its settings, API keys, conversations, or memory.

## Current status

The first Android host and Avalonia UI shell are in place. The next implementation stages are:

1. Port Capri's personality and NVIDIA-compatible AI client into mobile-owned code.
2. Add private on-device conversation history and memory storage.
3. Add Android microphone permission, speech input, and spoken replies.
4. Add the mobile avatar/animation layer.
5. Add optional screen capture and overlay features using Android's permission APIs.

Desktop-only controls and the desktop cross project are intentionally not referenced by this project.

## Build prerequisites

- .NET 10 SDK
- .NET Android workload
- Android SDK / platform tools
- JDK 17 or later

Install the workload in PowerShell:

```powershell
dotnet workload install android
```

Build from the repository root:

```powershell
dotnet restore .\mobile\Capri.Mobile.csproj
dotnet build .\mobile\Capri.Mobile.csproj -c Debug
```

For a connected phone, enable Developer options and USB debugging, connect it by USB, then check:

```powershell
adb devices
```

If the device is listed, install/run with:

```powershell
dotnet build .\mobile\Capri.Mobile.csproj -t:Install -f net10.0-android
```

The Samsung Galaxy S9 on Android 9 is below Avalonia's currently listed supported Android versions (Android 11+). Android 9 may still be testable depending on the .NET Android runtime and device image, but it is not an officially supported target and must be verified on the actual phone before investing in advanced features.
