# PhoneMic Android

Use VS Code to edit the Kotlin source and PowerShell to build. The app supports
Android 8.0 and newer. The project uses Android API 35, JDK 17, and the Gradle
8.9 Wrapper. It limits Gradle to two workers and a 768 MB Java heap.

## One-time setup

Download a Windows x64 JDK 17 ZIP and the Android SDK Command-Line Tools ZIP.
Extract the JDK so `.phonemic-tools/jdk/<jdk-folder>/bin/javac.exe` exists.
Extract the Android tools so `.phonemic-tools/sdk/cmdline-tools/latest/bin/sdkmanager.bat`
exists. From the workspace root, run this in PowerShell:

```powershell
$root = (Resolve-Path .\.phonemic-tools).Path
$env:JAVA_HOME = (Get-ChildItem "$root\jdk" -Directory | Select-Object -First 1).FullName
$env:ANDROID_HOME = Join-Path $root 'sdk'
$env:ANDROID_USER_HOME = Join-Path $root 'android-user'
$manager = Join-Path $env:ANDROID_HOME 'cmdline-tools\latest\bin\sdkmanager.bat'
& $manager "--sdk_root=$env:ANDROID_HOME" 'platforms;android-35' 'build-tools;34.0.0' 'platform-tools'
```

Review and accept the Android SDK license when prompted. The first build
downloads Gradle and the build dependencies into `.phonemic-tools`.

## Build and install

From the workspace root, with the local JDK and SDK prepared:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\android\build.ps1
```

The debug APK is at `android/app/build/outputs/apk/debug/app-debug.apk`.
Transfer it to a physical Android phone or use Android platform-tools with
USB debugging authorized on the phone:

```powershell
.\.phonemic-tools\sdk\platform-tools\adb.exe devices
.\.phonemic-tools\sdk\platform-tools\adb.exe install -r .\android\app\build\outputs\apk\debug\app-debug.apk
```

## Test the receiver and microphone

1. Put the phone and Windows PC on the same trusted private LAN. Stop any
   existing Phase 2 receiver that is using port 8765.
2. Run `python audio_receiver.py --capture phonemic-test.wav` on the PC.
   Omit `--capture` to receive without writing audio. With capture enabled,
   each session that sends audio overwrites the file with up to 60 seconds.
3. In PhoneMic, enter the PC's IPv4 address and tap **Check receiver**.
   The existing dashboard runs on port 8765. Streaming uses TCP port 8766.
4. Tap **Connect**, allow microphone access, and allow notifications if
   you want to see the persistent status and Disconnect action.
5. Speak, mute and speak again, then unmute. Switch to another app or lock
   the screen, speak briefly, then disconnect. Play the WAV after disconnecting.
   Confirm speech is audible before and after mute, the muted interval is
   silent, and the notification disappears after disconnect.
6. Repeat once with microphone permission denied, and once with the receiver
   stopped, to verify that the app reports the failure and returns to idle.

Audio is mono 16-bit little-endian PCM at 16 kHz over the LAN. The prototype
uses cleartext and has no authentication. Keep both ports off the public
internet. If the dashboard check works but the audio connection fails,
check whether Windows permits TCP 8766 on the private network.

## Windows virtual microphone

The Windows PC has VB-Audio Virtual Cable endpoints. List the playback
devices, then start the receiver with the number for **CABLE Input**.

```powershell
python audio_receiver.py --list-output-devices
python audio_receiver.py --output-device 2
```

The number comes from the list and can change. Select **CABLE Output
(VB-Audio Virtual Cable)** as the microphone in the destination app. The
receiver uses Windows audio playback directly, with no extra Python packages.
You can combine `--output-device` with `--capture` to also save a test WAV.
