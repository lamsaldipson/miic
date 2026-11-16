"""Send PCM to an explicitly selected Windows playback device via WinMM."""
import ctypes as c
import time
from ctypes import wintypes as w

WINMM = c.WinDLL('winmm')
CALL = c.WINFUNCTYPE
DONE = 1
IN_QUEUE = 16
FRAME_BYTES = 640


class WaveFormat(c.Structure):
    _pack_ = 1
    _fields_ = [
        ('format', w.WORD), ('channels', w.WORD), ('rate', w.DWORD),
        ('bytes_per_second', w.DWORD), ('alignment', w.WORD), ('bits', w.WORD),
        ('extra_size', w.WORD),
    ]


class WaveHeader(c.Structure):
    _fields_ = [
        ('data', c.c_void_p), ('length', w.DWORD), ('recorded', w.DWORD),
        ('user', c.c_size_t), ('flags', w.DWORD), ('loops', w.DWORD),
        ('next', c.c_void_p), ('reserved', c.c_size_t),
    ]


class WaveCaps(c.Structure):
    _fields_ = [
        ('manufacturer', w.WORD), ('product', w.WORD), ('version', w.DWORD),
        ('name', w.WCHAR * 32), ('formats', w.DWORD), ('channels', w.WORD),
        ('reserved', w.WORD), ('support', w.DWORD),
    ]


def check(result, operation):
    if result:
        description = c.create_unicode_buffer(256)
        WINMM.waveOutGetErrorTextW(result, description, len(description))
        raise OSError(f'{operation}: {description.value or result}')


def devices():
    for index in range(WINMM.waveOutGetNumDevs()):
        caps = WaveCaps()
        check(WINMM.waveOutGetDevCapsW(c.c_size_t(index), c.byref(caps), c.sizeof(caps)), 'List audio device')
        yield index, caps.name


class Playback:
    def __init__(self, device, rate):
        self.handle = w.HANDLE()
        self.buffers = []
        self.index = 0
        fmt = WaveFormat(1, 1, rate, rate * 2, 2, 16, 0)
        check(WINMM.waveOutOpen(c.byref(self.handle), device, c.byref(fmt), None, None, 0),
              'Open playback device')

    def __enter__(self):
        try:
            for _ in range(4):
                buffer = c.create_string_buffer(FRAME_BYTES)
                header = WaveHeader(data=c.addressof(buffer), length=FRAME_BYTES)
                self.call('waveOutPrepareHeader', header)
                self.buffers.append((buffer, header))
        except OSError:
            self.__exit__(None, None, None)
            raise
        return self

    def call(self, operation, header):
        check(getattr(WINMM, operation)(self.handle, c.byref(header), c.sizeof(header)), operation)

    def write(self, frame):
        buffer, header = self.buffers[self.index]
        if header.flags & IN_QUEUE:
            deadline = time.monotonic() + 1
            while not header.flags & DONE:
                if time.monotonic() >= deadline:
                    raise OSError('Windows audio output stalled')
                time.sleep(0.002)
        c.memmove(buffer, frame, len(frame))
        header.length = len(frame)
        self.call('waveOutWrite', header)
        self.index = (self.index + 1) % len(self.buffers)

    def __exit__(self, *_):
        WINMM.waveOutReset(self.handle)
        for _, header in self.buffers:
            WINMM.waveOutUnprepareHeader(self.handle, c.byref(header), c.sizeof(header))
        WINMM.waveOutClose(self.handle)
