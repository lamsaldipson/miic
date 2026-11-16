# Build PhoneMic packages

The package uses the existing WinForms receiver and Android Gradle project.
Build from the repository root on Windows with JDK 17, Android SDK API 35,
Build Tools 34.0.0, and Inno Setup 6.7.4. Set up the local Android tools using
[the Android README](../android/README.md). Review Inno Setup's license before
commercial use.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\packaging\build-windows.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\packaging\build-android.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\packaging\finalize-release.ps1
```

Pass `-Compiler 'C:\path\to\ISCC.exe'` if needed. Setup installs to Program Files
by default and generates and registers its own uninstaller. Setup and the
standalone Windows binary are unsigned unless a publisher signs them.
Review the draft legal documents and sign the Windows binaries before public
distribution. Sign both Windows binaries before running the checksum step.

The Android script assembles the release variant, aligns it, signs it and
verifies the signature before copying it to `release/android/PhoneMic.apk`.
It accepts `-Keystore` and `-Alias`. It uses the
`PHONEMIC_KEY_PASSWORD` environment variable if already supplied, a local
DPAPI-protected password file for this Windows account if available, or an
interactive password prompt. This script expects a PKCS#12 key with a shared
store and key password. Keep the key and a secure backup of its password
outside Git. Back up the original key for updates. The DPAPI password file
works only for its original Windows user on its original machine.

VB-CABLE is detected by checking the standard playback and recording
endpoints and opening the playback endpoint. When absent, Setup links to the
official download and provides a recheck. Review the vendor's licensing page
and the license accompanying the chosen driver before changing the integration.
The downloaded Pack46 readme requires permission to integrate the package,
while the vendor's licensing page describes conditional redistribution.

## Test checklist

- Run Setup interactively to confirm acceptance, destination, shortcut defaults,
  VB-CABLE detection, finish and optional launch. After testing the startup
  task, verify the registry command launches with `--tray`.
- Test Windows Installed Apps removal with the app running. Verify files and
  selected shortcuts disappear, startup is removed, and VB-CABLE remains.
- Test a clean Windows machine with no VB-CABLE separately. Verify the vendor
  installer, restart flow, both audio endpoints and unchanged default output.
- Install the signed APK on a physical phone and confirm connect, mute and
  disconnect into CABLE Output.
