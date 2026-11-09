using System.Net;
using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

public class DownloadTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "br-dl-" + Guid.NewGuid().ToString("N"));
    static readonly byte[] Payload = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7)).ToArray();
    static readonly TimeSpan[] NoWait = { TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero };
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public DownloadTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch { }
    }

    /// <summary>Сервер, который отвечает по сценарию: каждый запрос — следующий шаг.</summary>
    sealed class ScriptedServer : IDisposable
    {
        readonly HttpListener listener = new();
        public readonly List<string?> Ranges = new();
        public string Url { get; }

        public ScriptedServer(params Func<HttpListenerContext, Task>[] steps)
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            Url = $"http://127.0.0.1:{port}/file.zip";
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            _ = Task.Run(async () =>
            {
                for (int i = 0; ; i++)
                {
                    HttpListenerContext ctx;
                    try { ctx = await listener.GetContextAsync(); } catch { return; }
                    Ranges.Add(ctx.Request.Headers["Range"]);
                    try { await steps[Math.Min(i, steps.Length - 1)](ctx); } catch { }
                }
            });
        }

        public void Dispose() => listener.Close();
    }

    static Func<HttpListenerContext, Task> Status(int code) => ctx =>
    {
        ctx.Response.StatusCode = code;
        ctx.Response.Close();
        return Task.CompletedTask;
    };

    static Func<HttpListenerContext, Task> Full() => async ctx =>
    {
        ctx.Response.ContentLength64 = Payload.Length;
        await ctx.Response.OutputStream.WriteAsync(Payload);
        ctx.Response.Close();
    };

    /// <summary>Объявить весь размер, отдать половину и оборвать соединение.</summary>
    static Func<HttpListenerContext, Task> HalfThenDrop() => async ctx =>
    {
        ctx.Response.ContentLength64 = Payload.Length;
        await ctx.Response.OutputStream.WriteAsync(Payload.AsMemory(0, Payload.Length / 2));
        await ctx.Response.OutputStream.FlushAsync();
        ctx.Response.Abort();
    };

    /// <summary>Докачка: 206 с остатком от запрошенного места.</summary>
    static Func<HttpListenerContext, Task> Resume() => async ctx =>
    {
        long from = long.Parse(ctx.Request.Headers["Range"]!.Replace("bytes=", "").TrimEnd('-'));
        ctx.Response.StatusCode = 206;
        ctx.Response.ContentLength64 = Payload.Length - from;
        ctx.Response.AddHeader("Content-Range", $"bytes {from}-{Payload.Length - 1}/{Payload.Length}");
        await ctx.Response.OutputStream.WriteAsync(Payload.AsMemory((int)from));
        ctx.Response.Close();
    };

    [Fact]
    public async Task Retries_after_504_and_reports_attempts()
    {
        using var server = new ScriptedServer(Status(504), Status(503), Full());
        var attempts = new List<string>();
        string path = Path.Combine(dir, "a.zip");
        await Downloads.ToFileAsync(Http, server.Url, path, Payload.Length, null, (a, t) => attempts.Add($"{a}/{t}"), CancellationToken.None, NoWait);
        Assert.Equal(Payload, File.ReadAllBytes(path));
        Assert.Equal(new[] { "2/4", "3/4" }, attempts);
    }

    [Fact]
    public async Task Broken_connection_resumes_from_where_it_stopped()
    {
        using var server = new ScriptedServer(HalfThenDrop(), Resume());
        string path = Path.Combine(dir, "b.zip");
        var reports = new List<double>();
        await Downloads.ToFileAsync(Http, server.Url, path, Payload.Length, new SyncProgress(reports), null, CancellationToken.None, NoWait);
        Assert.Equal(Payload, File.ReadAllBytes(path));
        Assert.Null(server.Ranges[0]);
        Assert.StartsWith("bytes=", server.Ranges[1]);                 // вторая попытка — докачка, а не заново
        Assert.Equal(1.0, reports[^1], 3);
    }

    [Fact]
    public async Task Gives_up_with_clear_message_after_all_attempts()
    {
        using var server = new ScriptedServer(Status(504));
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Downloads.ToFileAsync(Http, server.Url, Path.Combine(dir, "c.zip"), 0, null, null, CancellationToken.None, NoWait));
        Assert.Contains("ошибка 504", e.Message);
        Assert.Contains("4 попытки", e.Message);
        Assert.Equal(4, server.Ranges.Count);
    }

    [Fact]
    public async Task Not_found_is_not_retried()
    {
        using var server = new ScriptedServer(Status(404));
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Downloads.ToFileAsync(Http, server.Url, Path.Combine(dir, "d.zip"), 0, null, null, CancellationToken.None, NoWait));
        Assert.Contains("404", e.Message);
        Assert.Single(server.Ranges);
    }

    /// <summary>Progress&lt;T&gt; шлёт отчёты асинхронно — в тесте нужен синхронный.</summary>
    sealed class SyncProgress(List<double> list) : IProgress<double>
    {
        public void Report(double value) { lock (list) list.Add(value); }
    }
}
