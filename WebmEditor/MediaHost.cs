using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace WebmEditor;

/// <summary>
/// Makes local files reachable by the WebView. Publish() returns candidate URLs, tried in order by the page:
///  1) a WebView2 virtual host mapped to the file's folder (Windows only), and
///  2) a tiny loopback-only HTTP server (127.0.0.1) with Range + CORS support.
/// </summary>
public static class MediaHost
{
    public static Action<string, string>? MapFolder;

    private static int _n;
    private static TcpListener? _listener;
    private static readonly Dictionary<string, string> Files = new();

    public static string[] Publish(string filePath)
    {
        var urls = new List<string>();
        string name = Uri.EscapeDataString(System.IO.Path.GetFileName(filePath));

        if (MapFolder != null)
        {
            try
            {
                string host = $"m{++_n}.appassets.example";
                MapFolder(host, System.IO.Path.GetDirectoryName(filePath)!);
                urls.Add($"https://{host}/{name}");
            }
            catch { /* fall through to the HTTP server */ }
        }

        EnsureServer();
        string id = (++_n).ToString();
        lock (Files) Files[id] = filePath;
        int port = ((IPEndPoint)_listener!.LocalEndpoint).Port;
        urls.Add($"http://127.0.0.1:{port}/{id}/{name}");
        return urls.ToArray();
    }

    private static void EnsureServer()
    {
        if (_listener != null) return;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _ = Task.Run(async () =>
        {
            while (true)
            {
                TcpClient c;
                try { c = await _listener.AcceptTcpClientAsync(); } catch { return; }
                _ = Task.Run(() => HandleAsync(c));
            }
        });
    }

    private static string MimeFor(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".webm" => "video/webm",
        ".wav" => "audio/wav",
        ".mp3" => "audio/mpeg",
        ".ogg" => "audio/ogg",
        ".flac" => "audio/flac",
        ".m4a" => "audio/mp4",
        _ => "application/octet-stream"
    };

    private static async Task SendHead(NetworkStream ns, string status, string? mime, long length, string? extra = null)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {status}\r\n");
        if (mime != null) sb.Append($"Content-Type: {mime}\r\n");
        sb.Append($"Content-Length: {length}\r\n");
        sb.Append("Accept-Ranges: bytes\r\n");
        sb.Append("Access-Control-Allow-Origin: *\r\n");
        sb.Append("Access-Control-Allow-Headers: *\r\n");
        sb.Append("Cache-Control: no-store\r\n");
        sb.Append("Connection: close\r\n");
        if (extra != null) sb.Append(extra);
        sb.Append("\r\n");
        var bytes = Encoding.ASCII.GetBytes(sb.ToString());
        await ns.WriteAsync(bytes, 0, bytes.Length);
    }

    private static async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var ns = client.GetStream();
                var buf = new byte[8192];
                int len = 0;
                while (true)
                {
                    int r = await ns.ReadAsync(buf, len, buf.Length - len);
                    if (r <= 0) return;
                    len += r;
                    if (Encoding.ASCII.GetString(buf, 0, len).Contains("\r\n\r\n")) break;
                    if (len == buf.Length) return;
                }

                var lines = Encoding.ASCII.GetString(buf, 0, len).Split("\r\n");
                var first = lines[0].Split(' ');
                string method = first[0];
                string target = first.Length > 1 ? first[1] : "/";

                if (method == "OPTIONS") { await SendHead(ns, "204 No Content", null, 0); return; }

                var seg = target.Split('?')[0].Split('/', StringSplitOptions.RemoveEmptyEntries);
                string? path = null;
                if (seg.Length >= 1) lock (Files) Files.TryGetValue(seg[0], out path);
                if (path == null || !File.Exists(path)) { await SendHead(ns, "404 Not Found", "text/plain", 0); return; }

                long size = new FileInfo(path).Length, start = 0, end = size - 1;
                bool partial = false;
                var rangeLine = lines.FirstOrDefault(l => l.StartsWith("Range:", StringComparison.OrdinalIgnoreCase));
                if (rangeLine != null)
                {
                    var m = Regex.Match(rangeLine, @"bytes=(\d*)-(\d*)");
                    if (m.Success)
                    {
                        partial = true;
                        if (m.Groups[1].Value != "")
                        {
                            start = long.Parse(m.Groups[1].Value);
                            if (m.Groups[2].Value != "") end = Math.Min(end, long.Parse(m.Groups[2].Value));
                        }
                        else if (m.Groups[2].Value != "")
                        {
                            start = Math.Max(0, size - long.Parse(m.Groups[2].Value));
                        }
                    }
                }
                if (start >= size || start > end)
                {
                    await SendHead(ns, "416 Range Not Satisfiable", null, 0, $"Content-Range: bytes */{size}\r\n");
                    return;
                }

                long count = end - start + 1;
                if (partial) await SendHead(ns, "206 Partial Content", MimeFor(path), count, $"Content-Range: bytes {start}-{end}/{size}\r\n");
                else await SendHead(ns, "200 OK", MimeFor(path), count);
                if (method == "HEAD") return;

                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fs.Seek(start, SeekOrigin.Begin);
                var chunk = new byte[81920];
                long remaining = count;
                while (remaining > 0)
                {
                    int n = await fs.ReadAsync(chunk, 0, (int)Math.Min(chunk.Length, remaining));
                    if (n <= 0) break;
                    await ns.WriteAsync(chunk, 0, n);
                    remaining -= n;
                }
            }
            catch { /* the player closes connections when seeking; that's normal */ }
        }
    }
}
