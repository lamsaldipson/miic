"""Run the Phase 2 dashboard alongside a single PhoneMic PCM connection."""
import argparse
import socket
import threading
import time
import wave
from contextlib import ExitStack
from http.server import ThreadingHTTPServer

import receiver

AUDIO_PORT = 8766
SAMPLE_RATE = 16000
MAX_CAPTURE_BYTES = SAMPLE_RATE * 2 * 60


def receive_audio(listener, capture_path, output_device=None):
    while True:
        try:
            client, (ip, _) = listener.accept()
        except OSError:
            return
        try:
            with client, ExitStack() as stack:
                client.settimeout(5)
                if client.recv(1) != b'P':
                    continue
                playback = None
                if output_device is not None:
                    from windows_playback import FRAME_BYTES, Playback

                    playback = stack.enter_context(Playback(output_device, SAMPLE_RATE))
                client.sendall(b'\x06')
                print(f'PhoneMic audio connected from {ip}', flush=True)
                capture = None
                remaining = MAX_CAPTURE_BYTES
                carry = b''
                playback_buffer = bytearray()
                while chunk := client.recv(4096):
                    with receiver.lock:
                        receiver.last_seen[ip] = time.monotonic()
                    if playback is not None:
                        playback_buffer.extend(chunk)
                        count = len(playback_buffer) // FRAME_BYTES * FRAME_BYTES
                        for offset in range(0, count, FRAME_BYTES):
                            playback.write(bytes(playback_buffer[offset:offset + FRAME_BYTES]))
                        del playback_buffer[:count]
                    if capture_path and remaining:
                        if capture is None:
                            capture = stack.enter_context(wave.open(capture_path, 'wb'))
                            capture.setparams((1, 2, SAMPLE_RATE, 0, 'NONE', 'not compressed'))
                        data = carry + chunk
                        count = min(len(data) & ~1, remaining)
                        capture.writeframesraw(data[:count])
                        remaining -= count
                        carry = data[count:]
        except (OSError, wave.Error) as error:
            print(f'Audio session ended ({ip}): {error}', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--capture', metavar='FILE.wav', help='Overwrite with up to 60 seconds of audio per session')
    parser.add_argument('--list-output-devices', action='store_true')
    parser.add_argument('--output-device', type=int, help='Play audio to this Windows device ID')
    options = parser.parse_args()
    if options.list_output_devices or options.output_device is not None:
        import sys
        if sys.platform != 'win32':
            parser.error('Live playback requires Windows')
        from windows_playback import devices

        available = dict(devices())
        if options.list_output_devices:
            for index, name in available.items():
                print(f'{index}: {name}')
            return
        if options.output_device not in available:
            parser.error('Unknown output device; run with --list-output-devices')
        print(f'Playback device: {available[options.output_device]}', flush=True)
    with ThreadingHTTPServer((receiver.HOST, receiver.PORT), receiver.Handler) as http:
        with socket.create_server((receiver.HOST, AUDIO_PORT)) as audio:
            threading.Thread(target=receive_audio, args=(audio, options.capture, options.output_device), daemon=True).start()
            print(f'Dashboard on port {receiver.PORT}, PCM audio on port {AUDIO_PORT}', flush=True)
            try:
                http.serve_forever()
            except KeyboardInterrupt:
                pass


if __name__ == '__main__':
    main()
