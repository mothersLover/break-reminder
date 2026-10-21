using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

public class ImageTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "br-img-" + Guid.NewGuid().ToString("N"));
    static readonly DateTime Now = new(2026, 10, 22, 19, 0, 0);
    // PNG 1×1
    static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public ImageTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch { }
    }

    string Photo(string name = "page.png")
    {
        string p = Path.Combine(dir, name);
        File.WriteAllBytes(p, Png);
        return p;
    }

    static ChatMessage U(string t, params string[] images) => new(true, t, Now, Images: images.Length > 0 ? images : null);
    static ChatMessage A(string t) => new(false, t, Now);

    [Fact]
    public void ForModel_merges_images_and_keeps_only_recent_ones()
    {
        var chat = new[] { U("", "a.png"), U("упр. 3", "b.png"), A("ок"), U("дальше", "c.png"), A("ок"), U("ещё", "d.png") };
        var list = AiClients.ForModel(chat, keepImages: 2);
        Assert.Null(list[0].Images);                                     // старые фото — только пометкой
        Assert.StartsWith("[приложено фото: 2] упр. 3", list[0].Text);
        Assert.Equal(new[] { "c.png" }, list[2].Images);
        Assert.Equal(new[] { "d.png" }, list[4].Images);

        var photoOnly = AiClients.ForModel(new[] { U("", "z.png") });
        Assert.Single(photoOnly);                                       // фото без текста не теряется
    }

    [Fact]
    public async Task OpenAi_sends_image_as_data_url()
    {
        var (port, server, listener) = Serve("{\"choices\":[{\"message\":{\"content\":\"Вижу страницу\"}}]}", out var received);
        using var _ = listener;
        var reply = await new OpenAiCompatClient($"http://127.0.0.1:{port}/v1", "", "vision").CompleteAsync("SYS", new[] { U("проверь", Photo()) });
        await server;
        Assert.Equal("Вижу страницу", reply);
        var content = received()!["messages"]![1]!["content"]!.AsArray();
        Assert.Equal("image_url", content[0]!["type"]!.GetValue<string>());
        Assert.StartsWith("data:image/png;base64,iVBOR", content[0]!["image_url"]!["url"]!.GetValue<string>());
        Assert.Equal("проверь", content[1]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Anthropic_sdk_sends_image_block()
    {
        const string response = "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"claude-opus-5-5\"," +
                                "\"content\":[{\"type\":\"text\",\"text\":\"Вижу фото\"}],\"stop_reason\":\"end_turn\",\"stop_sequence\":null," +
                                "\"usage\":{\"input_tokens\":10,\"output_tokens\":3}}";
        var (port, server, listener) = Serve(response, out var received);
        using var _ = listener;
        var reply = await new AnthropicAiClient("test-key", "claude-opus-5-5", $"http://127.0.0.1:{port}")
            .CompleteAsync("SYS", new[] { U("", Photo("p.jpg")) });
        await server;
        Assert.Equal("Вижу фото", reply);
        var body = received()!;
        Assert.Equal("SYS", body["system"]!.GetValue<string>());
        var content = body["messages"]![0]!["content"]!.AsArray();
        Assert.Equal("image", content[0]!["type"]!.GetValue<string>());
        Assert.Equal("base64", content[0]!["source"]!["type"]!.GetValue<string>());
        Assert.Equal("image/jpeg", content[0]!["source"]!["media_type"]!.GetValue<string>());
        Assert.Equal(Convert.ToBase64String(Png), content[0]!["source"]!["data"]!.GetValue<string>());
        Assert.Equal("Посмотри фото.", content[1]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Claude_code_reads_images_with_read_tool_only_in_their_folder()
    {
        var args = ClaudeCodeClient.Arguments("", new[] { dir });
        Assert.Equal("Read", args[args.IndexOf("--tools") + 1]);
        Assert.Equal("Read", args[args.IndexOf("--allowedTools") + 1]);
        Assert.Equal(dir, args[args.IndexOf("--add-dir") + 1]);
        var plain = ClaudeCodeClient.Arguments("");
        Assert.Equal("", plain[plain.IndexOf("--tools") + 1]);          // без фото — никаких инструментов
        Assert.DoesNotContain("--add-dir", plain);

        string prompt = ClaudeCodeClient.BuildPrompt("SYS", new[] { U("вот задание", Path.Combine(dir, "p.png")) });
        Assert.Contains("[приложено фото — открой и посмотри: " + Path.Combine(dir, "p.png") + "]", prompt);
    }

    [Fact]
    public void Memory_marks_photos_in_summary_and_clears_them_with_chat()
    {
        var m = new TutorMemory(dir);
        Directory.CreateDirectory(m.ImagesDir);
        string photo = Path.Combine(m.ImagesDir, "x.png");
        File.WriteAllBytes(photo, Png);
        m.Add(U("упр. 5", photo));
        m.Add(A("Ошибка в третьем"));
        Assert.Contains("Человек: [приложено фото: 1] упр. 5", m.SummaryRequest(Now)!.Value.Request.Text);
        Assert.Equal(new[] { photo }, new TutorMemory(dir).Chat[0].Images);   // путь к фото сохраняется в истории
        m.ClearChat();
        Assert.False(Directory.Exists(m.ImagesDir));
    }

    [Fact]
    public void Homework_is_logged_and_counts_for_streak()
    {
        var log = new PracticeLog(Path.Combine(dir, "p.csv"));
        log.Done(Now, PracticeKind.Homework, 35);
        var again = new PracticeLog(Path.Combine(dir, "p.csv"));
        Assert.Equal(PracticeKind.Homework, again.Entries[0].Kind);
        Assert.Equal(35, again.MinutesOn(Now));
        Assert.Equal(1, again.Streak(Now));
        Assert.Contains("домашнее задание", PracticeText.Instructions(PracticeKind.Homework, 0));
    }

    /// <summary>Поддельный сервер на один запрос: запоминает тело, отвечает JSON.</summary>
    static (int Port, Task Server, IDisposable Listener) Serve(string response, out Func<JsonNode?> received)
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        JsonNode? body = null;
        received = () => body;
        var task = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            using (var r = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) body = JsonNode.Parse(await r.ReadToEndAsync());
            var bytes = Encoding.UTF8.GetBytes(response);
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        });
        return (port, task, listener);
    }
}
