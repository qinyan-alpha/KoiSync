using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace KoiSync.Studio
{
    internal sealed class StudioHttpRequest
    {
        internal string Method = "GET";
        internal string Path = "/";
        internal string Query = "";
        internal string Body = "";
        internal int StatusCode = 200;
        internal string ResponseJson = "{\"ok\":true}";
        internal bool Deferred;
        private readonly object waitGate = new object();
        private bool completed;

        internal void Complete(int statusCode, string responseJson)
        {
            lock (waitGate)
            {
                StatusCode = statusCode;
                ResponseJson = responseJson;
                completed = true;
                Monitor.PulseAll(waitGate);
            }
        }

        internal void SignalDone()
        {
            lock (waitGate)
            {
                completed = true;
                Monitor.PulseAll(waitGate);
            }
        }

        internal bool WaitDone(int timeoutMs)
        {
            lock (waitGate)
            {
                if (completed) return true;
                Monitor.Wait(waitGate, timeoutMs);
                return completed;
            }
        }
    }

    // Minimal loopback-only (127.0.0.1) HTTP/1.1 JSON bridge for CharaStudio (net35 / Unity 5.6).
    // All Timeline, scene, and recording handlers run on Unity's main thread via Pump().
    internal sealed class StudioDirectorBridge : IDisposable
    {
        private readonly object queueLock = new object();
        private readonly Queue<StudioHttpRequest> pending = new Queue<StudioHttpRequest>();
        private readonly Func<StudioHttpRequest, string> handler;
        private readonly Action<string> log;
        private TcpListener listener;
        private Thread acceptThread;
        private volatile bool running;
        private int port;

        internal bool Listening { get { return running; } }
        internal int Port { get { return port; } }
        internal string LastError { get; private set; }

        internal StudioDirectorBridge(Func<StudioHttpRequest, string> mainThreadHandler, Action<string> logger)
        {
            handler = mainThreadHandler;
            log = logger ?? (_ => { });
            LastError = "";
        }

        internal bool Start(int listenPort)
        {
            Stop();
            port = listenPort < 1024 || listenPort > 65535 ? 41290 : listenPort;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start(8);
                running = true;
                LastError = "";
                acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "KoiSyncStudioDirectorBridge" };
                acceptThread.Start();
                log("Studio AI Director local bridge listening on http://127.0.0.1:" + port);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.GetBaseException().Message;
                log("Studio AI Director bridge failed to bind 127.0.0.1:" + port + " -> " + LastError);
                Stop();
                return false;
            }
        }

        internal void Stop()
        {
            running = false;
            try { if (listener != null) listener.Stop(); } catch { }
            listener = null;
            lock (queueLock)
            {
                while (pending.Count > 0)
                {
                    var req = pending.Dequeue();
                    req.Complete(503, "{\"ok\":false,\"error\":\"Studio director bridge stopped\"}");
                }
            }
            if (acceptThread != null && acceptThread.IsAlive)
            {
                try { acceptThread.Join(200); } catch { }
            }
            acceptThread = null;
        }

        public void Dispose()
        {
            Stop();
        }

        internal void Pump(int maxPerFrame = 6)
        {
            for (int i = 0; i < maxPerFrame; i++)
            {
                StudioHttpRequest req = null;
                lock (queueLock)
                {
                    if (pending.Count > 0) req = pending.Dequeue();
                }
                if (req == null) break;
                try
                {
                    string resp = handler(req);
                    if (!req.Deferred) req.ResponseJson = resp;
                }
                catch (ArgumentException ex)
                {
                    req.Deferred = false;
                    req.StatusCode = 400;
                    req.ResponseJson = ErrorJson(ex.GetBaseException().Message);
                }
                catch (InvalidOperationException ex)
                {
                    req.Deferred = false;
                    req.StatusCode = 409;
                    req.ResponseJson = ErrorJson(ex.GetBaseException().Message);
                }
                catch (Exception ex)
                {
                    req.Deferred = false;
                    req.StatusCode = 500;
                    req.ResponseJson = ErrorJson(ex.GetBaseException().Message);
                }
                finally
                {
                    if (!req.Deferred) req.SignalDone();
                }
            }
        }

        private void AcceptLoop()
        {
            while (running)
            {
                TcpClient client = null;
                try
                {
                    if (listener == null) break;
                    client = listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    if (!running) break;
                    Thread.Sleep(50);
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch
                {
                    if (!running) break;
                    Thread.Sleep(50);
                    continue;
                }
                if (client == null) continue;
                var captured = client;
                ThreadPool.QueueUserWorkItem(_ => HandleClient(captured));
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 6000;
                    client.SendTimeout = 6000;
                    using (var stream = client.GetStream())
                    {
                        var req = ReadHttpRequest(stream);
                        if (req == null)
                        {
                            WriteHttpResponse(stream, 400, ErrorJson("Malformed HTTP request"));
                            return;
                        }
                        if (!running)
                        {
                            WriteHttpResponse(stream, 503, ErrorJson("Studio director bridge disabled"));
                            return;
                        }
                        lock (queueLock) pending.Enqueue(req);
                        if (!req.WaitDone(8000))
                        {
                            WriteHttpResponse(stream, 504, ErrorJson("CharaStudio main thread timeout"));
                            return;
                        }
                        WriteHttpResponse(stream, req.StatusCode, req.ResponseJson);
                    }
                }
                catch { }
            }
        }

        private static StudioHttpRequest ReadHttpRequest(NetworkStream stream)
        {
            var headerBytes = new MemoryStream();
            var buf = new byte[1];
            int matched = 0;
            const int maxHeader = 16384;
            while (headerBytes.Length < maxHeader)
            {
                int n = stream.Read(buf, 0, 1);
                if (n <= 0) return null;
                byte b = buf[0];
                headerBytes.WriteByte(b);
                if ((matched == 0 || matched == 2) && b == '\r') matched++;
                else if ((matched == 1 || matched == 3) && b == '\n') matched++;
                else matched = b == '\r' ? 1 : 0;
                if (matched == 4) break;
            }
            if (matched != 4) return null;

            string headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return null;
            var requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2) return null;

            string method = requestLine[0].Trim().ToUpperInvariant();
            string rawUrl = requestLine[1].Trim();
            string path = rawUrl;
            string query = "";
            int qIdx = rawUrl.IndexOf('?');
            if (qIdx >= 0) { path = rawUrl.Substring(0, qIdx); query = rawUrl.Substring(qIdx + 1); }

            int contentLength = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                string key = lines[i].Substring(0, colon).Trim();
                if (string.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(lines[i].Substring(colon + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out contentLength);
            }
            if (contentLength < 0 || contentLength > 1048576) return null;

            string body = "";
            if (contentLength > 0)
            {
                var bodyBytes = new byte[contentLength];
                int read = 0;
                while (read < contentLength)
                {
                    int n = stream.Read(bodyBytes, read, contentLength - read);
                    if (n <= 0) return null;
                    read += n;
                }
                body = Encoding.UTF8.GetString(bodyBytes);
            }

            return new StudioHttpRequest { Method = method, Path = path, Query = query, Body = body };
        }

        private static void WriteHttpResponse(NetworkStream stream, int statusCode, string jsonBody)
        {
            byte[] payload = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
            string reason = statusCode == 200 ? "OK" : statusCode == 400 ? "Bad Request" : statusCode == 404 ? "Not Found" : statusCode == 409 ? "Conflict" : statusCode == 503 ? "Service Unavailable" : statusCode == 504 ? "Gateway Timeout" : "Internal Server Error";
            string header =
                "HTTP/1.1 " + statusCode + " " + reason + "\r\n" +
                "Content-Type: application/json; charset=utf-8\r\n" +
                "Content-Length: " + payload.Length + "\r\n" +
                "Connection: close\r\n\r\n";
            byte[] hBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(hBytes, 0, hBytes.Length);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        internal static string ErrorJson(string message)
        {
            return "{\"ok\":false,\"error\":\"" + EscapeJson(message ?? "Unknown error") + "\"}";
        }

        internal static string EscapeJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length + 8);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
