import json
import socket
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse

HOST = '0.0.0.0'
PORT = 8765
ROOT = Path(__file__).resolve().parent
started = time.monotonic()
lock = threading.Lock()
last_seen = {}

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        path = urlparse(self.path).path
        if path == '/':
            data = (ROOT / 'dashboard.html').read_bytes()
            return self.respond(200, 'text/html; charset=utf-8', data)
        if path == '/api/status':
            with lock:
                devices = [{'ip': ip, 'seconds_ago': round(time.monotonic() - seen, 1)} for ip, seen in last_seen.items() if time.monotonic() - seen < 30]
            payload = {'status': 'running', 'host': socket.gethostname(), 'uptime_seconds': round(time.monotonic() - started), 'devices': devices, 'audio': 'not implemented'}
            return self.respond(200, 'application/json', json.dumps(payload).encode())
        if path == '/api/hello':
            ip = self.client_address[0]
            with lock:
                last_seen[ip] = time.monotonic()
            return self.respond(200, 'application/json', json.dumps({'ok': True, 'ip': ip}).encode())
        return self.respond(404, 'text/plain', b'Not found')

    def respond(self, status, content_type, data):
        self.send_response(status)
        self.send_header('Content-Type', content_type)
        self.send_header('Content-Length', str(len(data)))
        self.send_header('Cache-Control', 'no-store')
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, fmt, *args):
        print('%s - %s' % (self.address_string(), fmt % args))

if __name__ == '__main__':
    server = ThreadingHTTPServer((HOST, PORT), Handler)
    print(f'PhoneMic receiver ready: http://127.0.0.1:{PORT}')
    print('Press Ctrl+C to stop.')
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
