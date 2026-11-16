using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

sealed class Playback : IDisposable
{
    const int FrameBytes = 640;
    const int InQueue = 16;
    readonly IntPtr[] headers = new IntPtr[4];
    readonly IntPtr[] buffers = new IntPtr[4];
    readonly bool[] prepared = new bool[4];
    readonly int headerSize = Marshal.SizeOf(typeof(Header));
    readonly int flagsOffset = Marshal.OffsetOf(typeof(Header), "Flags").ToInt32();
    IntPtr handle;
    int index;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct Format {
        public ushort Tag, Channels;
        public uint SamplesPerSecond, BytesPerSecond;
        public ushort Alignment, Bits, Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Header {
        public IntPtr Data;
        public uint Length, Recorded;
        public IntPtr User;
        public uint Flags, Loops;
        public IntPtr Next, Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Capabilities {
        public ushort Manufacturer, Product;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;
        public uint Formats;
        public ushort Channels, Reserved;
        public uint Support;
    }

    [DllImport("winmm.dll")] static extern uint waveOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern uint waveOutGetDevCaps(UIntPtr id, out Capabilities caps, uint size);
    [DllImport("winmm.dll")] static extern uint waveOutOpen(out IntPtr handle, uint id, ref Format format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] static extern uint waveOutPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveOutWrite(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveOutReset(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint waveOutClose(IntPtr handle);

    public static IEnumerable<KeyValuePair<int, string>> Devices()
    {
        uint size = (uint)Marshal.SizeOf(typeof(Capabilities));
        for (uint i = 0; i < waveOutGetNumDevs(); i++) {
            Capabilities caps;
            if (waveOutGetDevCaps((UIntPtr)i, out caps, size) == 0)
                yield return new KeyValuePair<int, string>((int)i, caps.Name);
        }
    }

    [DllImport("winmm.dll")] static extern uint waveInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern uint waveInGetDevCaps(UIntPtr id, out Capabilities caps, uint size);

    internal static bool CableAvailable()
    {
        bool recordingEndpoint = false;
        uint size = (uint)Marshal.OffsetOf(typeof(Capabilities), "Support").ToInt32();
        for (uint i = 0; i < waveInGetNumDevs(); i++) {
            Capabilities caps;
            if (waveInGetDevCaps((UIntPtr)i, out caps, size) == 0 &&
                caps.Name.StartsWith("CABLE Output (VB-Audio", StringComparison.OrdinalIgnoreCase)) {
                recordingEndpoint = true;
                break;
            }
        }
        if (!recordingEndpoint) return false;
        foreach (var device in Devices()) {
            if (!device.Value.StartsWith("CABLE Input (VB-Audio", StringComparison.OrdinalIgnoreCase)) continue;
            try {
                using (new Playback(device.Key)) { }
                return true;
            } catch (InvalidOperationException) { return false; }
        }
        return false;
    }
    static void Check(uint result, string action)
    {
        if (result != 0) throw new InvalidOperationException(action + " (Windows audio error " + result + ").");
    }

    public Playback(int device)
    {
        var format = new Format { Tag = 1, Channels = 1, SamplesPerSecond = 16000,
                                  BytesPerSecond = 32000, Alignment = 2, Bits = 16 };
        Check(waveOutOpen(out handle, (uint)device, ref format, IntPtr.Zero, IntPtr.Zero, 0),
              "Could not open the selected playback device");
        try {
            for (int i = 0; i < headers.Length; i++) {
                buffers[i] = Marshal.AllocHGlobal(FrameBytes);
                headers[i] = Marshal.AllocHGlobal(headerSize);
                Marshal.StructureToPtr(new Header { Data = buffers[i], Length = FrameBytes }, headers[i], false);
                Check(waveOutPrepareHeader(handle, headers[i], (uint)headerSize), "Could not prepare audio");
                prepared[i] = true;
            }
        } catch {
            Dispose();
            throw;
        }
    }

    public void Write(byte[] frame)
    {
        var header = headers[index];
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while ((Marshal.ReadInt32(header, flagsOffset) & InQueue) != 0) {
            if (timer.ElapsedMilliseconds > 1000) throw new InvalidOperationException("Windows audio output stalled.");
            Thread.Sleep(2);
        }
        Marshal.Copy(frame, 0, buffers[index], FrameBytes);
        Check(waveOutWrite(handle, header, (uint)headerSize), "Could not play audio");
        index = (index + 1) % headers.Length;
    }

    public void Dispose()
    {
        waveOutReset(handle);
        for (int i = 0; i < headers.Length; i++) {
            if (prepared[i]) waveOutUnprepareHeader(handle, headers[i], (uint)headerSize);
            if (headers[i] != IntPtr.Zero) Marshal.FreeHGlobal(headers[i]);
            if (buffers[i] != IntPtr.Zero) Marshal.FreeHGlobal(buffers[i]);
        }
        waveOutClose(handle);
    }
}
