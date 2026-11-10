PhoneMic Phase 2 — local receiver and dashboard prototype

Requirements: Python 3.9+ on Windows. No pip installs required.

1. Extract this folder somewhere convenient (for example C:\PhoneMic).
2. Stop the earlier test HTTP server on port 8765 with Ctrl+C.
3. Open Command Prompt, cd into the extracted folder, and run:
   python receiver.py
4. On PC open http://127.0.0.1:8765
5. On Android open http://192.168.18.94:8765 (update if PC IP changed).
6. On Android open http://192.168.18.94:8765/api/hello
7. Refresh the dashboard on the PC. Your phone's IP should show for 30 seconds.

This phase does not transmit audio. Do not expose the port to the internet.
The /api/hello endpoint has no authentication and is intended only for temporary testing on a trusted private LAN.
To stop the receiver, press Ctrl+C. Remove the temporary firewall rule when finished:
  netsh advfirewall firewall delete rule name="PhoneMic Test HTTP"
