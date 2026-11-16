# PhoneMic

Use your Android phone as a wireless microphone for Windows. The Windows app
receives audio over your network and routes it to a virtual microphone that
you can select in apps such as OBS, Discord, or Audacity.

## Download and install

Get **PhoneMicSetup.exe** and **PhoneMic.apk** from the
[Windows and Android preview release](https://github.com/lamsaldipson/miic/releases/tag/v0.3-preview).

1. **Windows 10 or 11 (64-bit).** Run **PhoneMicSetup.exe**. The wizard checks
   for VB-Audio Virtual Cable. If it is missing, follow the wizard's link to
   the official installer, then follow the vendor's restart instructions.
2. **Android 8.0 or newer.** Open **PhoneMic.apk** on the phone and follow
   Android's installation prompt. If you have a development build signed
   with a different key, uninstall it first. This clears its saved settings.
3. Put both devices on the same trusted private network. Open PhoneMic on
   Windows, choose **CABLE Input** as its audio output, and turn the receiver on.
4. Open PhoneMic on Android, tap **Check receiver**, and select your computer
   below the status. Grant microphone permission when asked. If discovery is
   unavailable, enter the PC's IPv4 address and tap **Connect by IP**.
5. In your Windows recording or calling app, select **CABLE Output
   (VB-Audio Virtual Cable)** as the microphone.

The Windows installer is unsigned, so Windows may show a publisher warning.
Only use the files from this release and compare their SHA-256 checksums.

## Example with OBS

If your PC receiver is named **Studio PC**, look for **Studio PC** with its IP
address below it on Android. Tap the row. When the status reads **Streaming**,
add an **Audio Input Capture** source in OBS and select **CABLE Output**.
Speak and check the meter. Tap **Mute** for silence or **Disconnect** to stop
streaming. Closing the Windows PhoneMic window keeps the receiver accessible
from its blue microphone icon in the notification area.

## Audio speed and sync

PhoneMic streams 16 kHz, 16-bit mono PCM over TCP. Audio payload is about
**256 kbps (32 kB/s)**, before network overhead. Android reads audio in blocks
of up to 320 samples, which is **20 ms of audio per full block**. That is the
block size, not the end-to-end delay. Streaming has been tested on a physical
phone, but end-to-end latency has not been benchmarked. Wi-Fi, device buffers,
the virtual cable, and the receiving app all affect how closely audio lines
up with video. For an OBS recording, clap on camera and compare the visual
clap to the audio peak to measure the offset on your setup.

## Terms and privacy

Publisher: **Bipsan Lamsam**. Use the app only with permission and on a trusted
network. The stream is unencrypted and there is no receiver authentication.
The packaged apps do not save microphone recordings by default.

- [Terms and conditions (EULA draft)](packaging/documentation/LICENSE.txt)
- [Privacy notice](packaging/documentation/PRIVACY.txt)
- [VB-CABLE and other third-party notices](packaging/documentation/THIRD_PARTY_NOTICES.txt)
- [Detailed installation and removal guide](packaging/documentation/INSTALLATION_GUIDE.txt)

The EULA and privacy notice are drafts pending legal review. For source builds,
see the [packaging instructions](packaging/README.md).
