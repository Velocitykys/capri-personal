# Capri Mobile (Android)

A standalone Android Capri. It does not connect to the Windows Capri installation and does not share its API key, settings, conversations, or memory.

## Implemented so far

- Separate .NET 10 / Avalonia 12 Android application.
- Android application and activity bootstrap.
- Capri chat screen with a mobile-friendly layout.
- NVIDIA-compatible OpenAI chat-completions client.
- User-supplied API key, endpoint, and model settings.
- Persistent conversation history in the app's private local data directory.
- Capri's personality and safety guidance carried into the mobile prompt.
- Android app backup disabled so private app data is not included in Android backup.

The API key is currently stored in the app's private local settings file. It is not committed to GitHub, but this first implementation does not yet encrypt it with Android Keystore. Do not share the app's private data.

## Not implemented yet

- Long-term memory extraction/retrieval equivalent to desktop Capri's memory system.
- Microphone input, speech-to-text, and spoken replies.
- Animated avatar and reactions.
- Android screen capture and floating overlay.
- Phone actions / accessibility integration.

These are separate Android-native features and will be added after the first APK builds and runs.

## Build on Windows

Prerequisites:
- .NET 10 SDK
- .NET Android workload
- Android SDK / platform tools
- JDK 17 or later

Install the workload:

```powershell
dotnet workload install android
```

Build from the repository root:

```powershell
dotnet restore .\mobile\Capri.Mobile.csproj
dotnet build .\mobile\Capri.Mobile.csproj -c Debug
```

## GitHub Actions build

A workflow at `.github/workflows/capri-mobile-android.yml` builds a debug APK whenever files under `mobile/` change on this branch. The APK is uploaded as the `capri-mobile-debug-apk` workflow artifact.

## Install on a phone

Enable Developer options and USB debugging on the phone, connect it by USB, then check:

```powershell
adb devices
```

Once the Android workload is installed locally, you can deploy directly with:

```powershell
dotnet build .\mobile\Capri.Mobile.csproj -t:Install -f net10.0-android
```

The Galaxy S9 runs Android 9 (API 28). Avalonia supports Android as a platform, but current Avalonia/.NET platform guidance places Android 11 and earlier in its lowest support tier. We will need to verify the actual APK on the S9; compatibility is not guaranteed by setting the minimum SDK alone.
