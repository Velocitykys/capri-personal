# Capri Mobile — Android

A standalone Android companion built specifically for phones. It does not connect to the Windows desktop installation and does not share its API key, settings, conversations, or local memory.

## Included in this mobile rebuild

- Native .NET Android application (no Avalonia runtime).
- Capri chat using an OpenAI-compatible NVIDIA endpoint.
- Independent endpoint, model, API-key and spoken-reply settings.
- Local conversation history.
- Persistent user-managed memory, plus lightweight automatic capture of explicit remember requests and common stable preferences.
- Relevant saved memories are added to the model context for recall.
- Speech-to-text using Android's speech recognizer.
- Spoken replies using Android TextToSpeech.
- Capri artwork in the chat header.
- Local scheduled reminders delivered as Android notifications.
- Android share-sheet integration: share text from another app into Capri, or share a Capri reply out.
- Private app storage and Android backup disabled.

## Important boundaries

- The AI conversation requires internet access and the user's own API key. On-device chat history, memories and reminders remain available offline, but this build does not bundle a local language model; it cannot generate full AI replies without a network connection.
- Voice recognition availability depends on an installed Android speech-recognition provider and its language support. Text-to-speech uses the phone's installed voice.
- This is not yet the full desktop Capri feature set. Android cannot directly run Windows-only screen watching, game hooks, desktop automation or the desktop overlay. Screen capture, a floating companion, accessibility-based phone control, encrypted key storage, robust semantic memory, background sync and an optional authenticated desktop bridge remain separate engineering tasks.
- Reminders are local alarms. Android battery-management settings may affect delivery, and alarms are not currently restored after a device reboot.
- The API key is stored in the app's private files directory. Backup is disabled, but this version does not yet wrap the key with Android Keystore encryption. Do not share app-private data.

## Build

Requires .NET 10 SDK, the .NET Android workload, Android SDK platform 35/build tools 35, and JDK 17.

```powershell
dotnet workload install android
dotnet restore .\mobile\Capri.Mobile.csproj
dotnet build .\mobile\Capri.Mobile.csproj -c Debug
```

GitHub Actions builds a debug APK on pushes to `capri/android-mobile` and uploads `capri-mobile-debug-apk`.
