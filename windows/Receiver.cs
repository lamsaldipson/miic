using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

sealed class Receiver : IDisposable
{
    readonly string name;
    readonly string identity;
    readonly int device;
    readonly TcpListener audio = new TcpListener(IPAddress.Any, 8766);
    readonly TcpListener http = new TcpListener(IPAddress.Any, 8765);
    readonly object gate = new object();
    readonly object seenGate = new object();
    readonly DateTime started = DateTime.UtcNow;
    readonly System.Collections.Generic.Dictionary<string, DateTime> seen =
        new System.Collections.Generic.Dictionary<string, DateTime>();
    UdpClient discovery;
    TcpClient active;
    volatile bool stopped;
    readonly Action<string> report;

    public Receiver(string name, string identity, int device, Action<string> report)
    {
        this.name = name;
        this.identity = identity;
        this.device = device;
        this.report = report;
        try {
            using (var probe = new Playback(device)) { }
            http.Start();
            audio.Start();
            discovery = new UdpClient(new IPEndPoint(IPAddress.Any, 8765));
            Start(Discover);
            Start(ServeHttp);
            Start(ReceiveAudio);
        } catch {
            Dispose();
            throw;
        }
    }

    static void Start(ThreadStart work) { new Thread(work) { IsBackground = true }.Start(); }

    void Discover()
    {
        var reply = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(
            new { protocol = "PhoneMic/1", id = identity, name = name }));
        while (!stopped) {
            try {
                IPEndPoint peer = null;
                var query = discovery.Receive(ref peer);
                if (Encoding.ASCII.GetString(query) == "PhoneMic discover/1")
                    discovery.Send(reply, reply.Length, peer);
            } catch (SocketException) { if (stopped) return; }
              catch (ObjectDisposedException) { return; }
        }
    }

    void Seen(string ip) { lock (seenGate) seen[ip] = DateTime.UtcNow; }

    void ServeHttp()
    {
        while (!stopped) {
            try {
                using (var client = http.AcceptTcpClient()) {
                    client.ReceiveTimeout = 1500;
                    client.SendTimeout = 1500;
                    var peer = (IPEndPoint)client.Client.RemoteEndPoint;
                    using (var stream = client.GetStream()) {
                        var line = ReadRequestLine(client, stream);
                        if (line == null) continue;
                        var request = line.Split(' ');
                        var path = request.Length >= 3 && request[0] == "GET" ? request[1] : "";
                        var json = new JavaScriptSerializer();
                        string type = "application/json; charset=utf-8";
                        byte[] payload;
                        string status = "200 OK";
                        if (path == "/api/hello") {
                            Seen(peer.Address.ToString());
                            payload = Encoding.UTF8.GetBytes(json.Serialize(new {
                                ok = true, ip = peer.Address.ToString(), name = name
                            }));
                        } else if (path == "/api/status") {
                            var now = DateTime.UtcNow;
                            lock (seenGate) {
                                foreach (var ip in seen.Where(item => (now - item.Value).TotalSeconds >= 30)
                                                       .Select(item => item.Key).ToArray()) seen.Remove(ip);
                                payload = Encoding.UTF8.GetBytes(json.Serialize(new {
                                    status = "running", host = name, uptime_seconds = (int)(now - started).TotalSeconds,
                                    devices = seen.Select(item => new { ip = item.Key, seconds_ago = (now - item.Value).TotalSeconds }).ToArray(),
                                    audio = active == null ? "ready" : "connected"
                                }));
                            }
                        } else if (path == "/") {
                            type = "text/html; charset=utf-8";
                            using (var page = typeof(Receiver).Assembly.GetManifestResourceStream("dashboard.html"))
                            using (var reader = new StreamReader(page)) {
                                payload = Encoding.UTF8.GetBytes(reader.ReadToEnd()
                                    .Replace("Windows Receiver · Phase 2", "Windows Receiver")
                                    .Replace("Not enabled yet", "Ready"));
                            }
                        } else {
                            status = "404 Not Found";
                            payload = Encoding.UTF8.GetBytes("{}");
                        }
                        var header = Encoding.ASCII.GetBytes("HTTP/1.0 " + status + "\r\nContent-Type: " + type
                            + "\r\nContent-Length: " + payload.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                        stream.Write(header, 0, header.Length);
                        stream.Write(payload, 0, payload.Length);
                        client.Client.Shutdown(SocketShutdown.Send);
                    }
                }
            } catch (IOException) { }
              catch (SocketException) { if (stopped) return; }
              catch (ObjectDisposedException) { if (stopped) return; }
        }
    }

    static string ReadRequestLine(TcpClient client, NetworkStream stream)
    {
        var buffer = new byte[8192];
        var timer = System.Diagnostics.Stopwatch.StartNew();
        int end = 0;
        while (end < buffer.Length) {
            int remaining = 1500 - (int)timer.ElapsedMilliseconds;
            if (remaining <= 0) return null;
            client.ReceiveTimeout = remaining;
            int count = stream.Read(buffer, end, buffer.Length - end);
            if (count == 0) return null;
            int start = Math.Max(0, end - 3);
            end += count;
            for (int i = start; i + 3 < end; i++) {
                if (buffer[i] == 13 && buffer[i + 1] == 10 && buffer[i + 2] == 13 && buffer[i + 3] == 10) {
                    int lineEnd = Array.IndexOf(buffer, (byte)13, 0, i + 1);
                    return Encoding.ASCII.GetString(buffer, 0, lineEnd);
                }
            }
        }
        return null;
    }
    void ReceiveAudio()
    {
        while (!stopped) {
            try {
                using (var client = audio.AcceptTcpClient()) {
                    lock (gate) {
                        if (stopped) return;
                        active = client;
                    }
                    var ip = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                    client.ReceiveTimeout = 4000;
                    client.NoDelay = true;
                    using (var stream = client.GetStream()) {
                        if (stream.ReadByte() != 'P') continue;
                        using (var output = new Playback(device)) {
                            stream.WriteByte(6);
                            var frame = new byte[640];
                            var count = 0;
                            var nextCheckIn = DateTime.MinValue;
                            while (!stopped) {
                                int read = stream.Read(frame, count, frame.Length - count);
                                if (read == 0) break;
                                count += read;
                                if (count != frame.Length) continue;
                                output.Write(frame);
                                count = 0;
                                var now = DateTime.UtcNow;
                                if (now >= nextCheckIn) {
                                    Seen(ip);
                                    nextCheckIn = now.AddSeconds(2);
                                }
                            }
                        }
                    }
                }
            } catch (IOException) { }
              catch (SocketException) { if (stopped) return; }
              catch (ObjectDisposedException) { if (stopped) return; }
              catch (InvalidOperationException error) { if (!stopped) report(error.Message); }
            finally { lock (gate) active = null; }
        }
    }

    public void Dispose()
    {
        lock (gate) {
            stopped = true;
            if (active != null) active.Close();
            if (discovery != null) discovery.Close();
            http.Stop();
            audio.Stop();
        }
    }
}
