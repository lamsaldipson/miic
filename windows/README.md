# PhoneMic for Windows

Double-click **PhoneMic.exe**. It starts with your Windows computer name and
selects **CABLE Input** when VB-Audio Virtual Cable is present. If you want a
different name or playback device, click **Turn off**, edit the settings, and
click **Turn on**. PhoneMic remembers the name and output you use.

Close or minimize the window to keep the receiver in the notification area.
Look under the **Show hidden icons** arrow if it is not pinned to the taskbar.
Double-click the microphone icon to reopen the window. Right-click it to turn
the receiver on or off, or select **Exit** to quit it.

On your phone, open the updated PhoneMic app on the same Wi-Fi network and
tap **Check receiver**. Tap the name and IP address below the status to stream
to that receiver. The list refreshes while PhoneMic is open. Turning the
receiver off removes its entry on the next successful refresh. If broadcasts
are blocked, enter the Windows IP and use **Connect by IP**.

Choose **CABLE Output (VB-Audio Virtual Cable)** as the microphone in the
Windows app that should receive the audio. The receiver uses local TCP ports
8765 and 8766 and UDP port 8765. Stop any older PhoneMic receiver before
launching it. If Windows asks, allow PhoneMic on private networks only.
The LAN stream is unencrypted and has no sign-in.

## Build the executable

From the workspace root, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\windows\build.ps1
```

The build uses the .NET Framework compiler included with Windows. The output
is `windows/PhoneMic.exe`, with the icon and dashboard embedded.
