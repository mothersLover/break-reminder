using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

public class TutorMemoryTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "br-ai-" + Guid.NewGuid().ToString("N"));
    static readonly DateTime Now = new(2026, 10, 21, 19, 30, 0);

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public void Profile_notes_and_chat_persist()
    {
        var m = new TutorMemory(dir);
        m.Profile = "  Учу французский по Édito A2, Unité 5  ";
        m.Notes = "Путает être/avoir";
        m.Add(new ChatMessage(true, "Bonjour!", Now));
        m.Add(new ChatMessage(false, "Salut ! Ça va ?", Now));

        var again = new TutorMemory(dir);
        Assert.Equal("Учу французский по Édito A2, Unité 5", again.Profile);
        Assert.Equal("Путает être/avoir", again.Notes);
        Assert.Equal(new[] { "Bonjour!", "Salut ! Ça va ?" }, again.Chat.Select(c => c.Text));
        Assert.True(again.Chat[0].IsUser);
        Assert.Contains("Édito", File.ReadAllText(Path.Combine(dir, "chat.json")) + File.ReadAllText(Path.Combine(dir, "profile.txt")));

        again.ForgetAll();
        var empty = new TutorMemory(dir);
        Assert.Equal("", empty.Profile);
        Assert.Equal("", empty.Notes);
        Assert.Empty(empty.Chat);
    }

    [Fact]
    public void Summary_covers_only_new_messages_and_survives_restart()
    {
        var m = new TutorMemory(dir);
        Assert.Null(m.SummaryRequest(Now));                       // нечего запоминать
        m.Add(new ChatMessage(true, "Проверь: j'ai allé au cinéma", Now));
        Assert.Null(m.SummaryRequest(Now));                       // ответа ещё не было
        m.Add(new ChatMessage(false, "Почти! С aller нужен être.", Now));

        var req = m.SummaryRequest(Now)!.Value;
        Assert.Contains("j'ai allé", req.Request.Text);
        Assert.Contains("(пока пусто)", req.Request.Text);
        Assert.Equal(2, req.Count);
        m.Notes = "Ошибки: être с глаголами движения";
        m.MarkSummarized(req.Count);
        Assert.False(new TutorMemory(dir).NeedsSummary);

        var again = new TutorMemory(dir);
        again.Add(new ChatMessage(true, "А je suis allée?", Now));
        again.Add(new ChatMessage(false, "Да, если ты женщина.", Now));
        var next = again.SummaryRequest(Now)!.Value;
        Assert.DoesNotContain("j'ai allé au", next.Request.Text); // старое уже в заметках
        Assert.Contains("je suis allée", next.Request.Text);
        Assert.Contains("Ошибки: être", next.Request.Text);
    }

    [Fact]
    public void System_prompt_contains_memory_and_style()
    {
        var m = new TutorMemory(dir);
        var s = new AppSettings { UserName = "Дима", AiHintsFirst = true };
        string empty = m.SystemPrompt(s, Now);
        Assert.Contains("ничего не знаешь", empty);
        Assert.Contains("Дима", empty);
        Assert.Contains("подсказку", empty);

        m.Profile = "Édito A2, Unité 5";
        m.Notes = "Пройдено: passé composé";
        s.AiHintsFirst = false;
        string full = m.SystemPrompt(s, Now);
        Assert.DoesNotContain("ничего не знаешь", full);
        Assert.Contains("Édito A2, Unité 5", full);
        Assert.Contains("Пройдено: passé composé", full);
        Assert.Contains("сразу показывай правильный", full);
    }
}

[Collection("AiClients.Gate")]
public class AiClientTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "br-cc-" + Guid.NewGuid().ToString("N"));
    static readonly DateTime Now = new(2026, 10, 21, 19, 30, 0);
    static ChatMessage U(string t) => new(true, t, Now);
    static ChatMessage A(string t) => new(false, t, Now);

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public void ForModel_drops_local_merges_repeats_and_starts_with_user()
    {
        var chat = new[] { A("приветствие"), U("раз"), U("два"), new ChatMessage(false, "ошибка", Now, Local: true), A("ответ"), U("три") };
        var list = AiClients.ForModel(chat);
        Assert.Equal(new[] { "раз\n\nдва", "ответ", "три" }, list.Select(m => m.Text));
        Assert.Equal(new[] { true, false, true }, list.Select(m => m.IsUser));

        var many = Enumerable.Range(0, 50).Select(i => i % 2 == 0 ? U("u" + i) : A("a" + i)).ToList();
        var cut = AiClients.ForModel(many, 10);
        Assert.True(cut.Count <= 10);
        Assert.True(cut[0].IsUser);
        Assert.Equal("a49", cut[^1].Text);
    }

    [Fact]
    public void Create_reports_missing_settings()
    {
        Assert.Throws<AiException>(() => AiClients.Create(new AppSettings { AiProvider = AiProvider.Anthropic }));
        Assert.Throws<AiException>(() => AiClients.Create(new AppSettings { AiProvider = AiProvider.Ollama }));
        Assert.IsType<AnthropicAiClient>(AiClients.Create(new AppSettings { AiProvider = AiProvider.Anthropic, AiAnthropicKey = "k" }));
    }

    [Fact]
    public void Ai_settings_roundtrip()
    {
        var s = new AppSettings { AiOn = true, AiProvider = AiProvider.Ollama, AiOllamaModel = "llama3.2", AiHintsFirst = false, AiAnthropicKey = "sk-x" };
        string f(string n) => Path.Combine(dir, n);
        s.Save(dir, f("settings.ini"), f("t.txt"), f("tl.txt"));
        var l = AppSettings.Load(f("settings.ini"), f("t.txt"), f("tl.txt"));
        Assert.True(l.AiOn);
        Assert.Equal(AiProvider.Ollama, l.AiProvider);
        Assert.Equal("llama3.2", l.AiOllamaModel);
        Assert.False(l.AiHintsFirst);
        Assert.Equal("sk-x", l.AiAnthropicKey);
        Assert.Equal(AiClients.DefaultAnthropicModel, l.AiAnthropicModel);
        Assert.False(new AppSettings().AiOn);                     // по умолчанию выключено
    }

    [Fact]
    public void Claude_code_result_parsing()
    {
        Assert.Equal("Bonjour", ClaudeCodeClient.ParseResult("{\"type\":\"result\",\"is_error\":false,\"result\":\" Bonjour \"}"));
        var e = Assert.Throws<AiException>(() => ClaudeCodeClient.ParseResult("{\"is_error\":true,\"result\":\"Not logged in · Please run /login\"}"));
        Assert.Contains("не залогинен", e.Message);
        Assert.Throws<AiException>(() => ClaudeCodeClient.ParseResult("", "boom"));
        Assert.Contains("--no-session-persistence", ClaudeCodeClient.Arguments(""));
        Assert.Equal("haiku", ClaudeCodeClient.Arguments(" haiku ")[^1]);

        string prompt = ClaudeCodeClient.BuildPrompt("ИНСТРУКЦИЯ", new[] { U("первое"), A("ответ"), U("последнее") });
        Assert.Contains("ИНСТРУКЦИЯ", prompt);
        Assert.Contains("Человек: первое", prompt);
        Assert.Contains("Помощник: ответ", prompt);
        Assert.EndsWith("последнее", prompt);
        Assert.DoesNotContain("Человек: последнее", prompt);
    }

    [Fact]
    public async Task Claude_code_runs_cli_with_prompt_on_stdin()
    {
        Directory.CreateDirectory(dir);
        string fake;
        if (OperatingSystem.IsWindows())
        {
            fake = Path.Combine(dir, "claude.cmd");
            File.WriteAllText(fake, "@more > \"%~dp0stdin.txt\"\r\n@echo %* > \"%~dp0args.txt\"\r\n" +
                                    "@echo {\"type\":\"result\",\"is_error\":false,\"result\":\"Bonjour\"}\r\n");
        }
        else
        {
            fake = Path.Combine(dir, "claude");
            File.WriteAllText(fake, "#!/bin/sh\ncat > \"$(dirname \"$0\")/stdin.txt\"\necho \"$@\" > \"$(dirname \"$0\")/args.txt\"\n" +
                                    "echo '{\"type\":\"result\",\"is_error\":false,\"result\":\"Bonjour\"}'\n");
            File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var client = new ClaudeCodeClient(fake, "haiku", Path.Combine(dir, "work"));
        string reply = await client.CompleteAsync("Be brief", new[] { U("Say hello in French") });
        Assert.Equal("Bonjour", reply);
        Assert.Contains("Say hello in French", File.ReadAllText(Path.Combine(dir, "stdin.txt")));
        string args = File.ReadAllText(Path.Combine(dir, "args.txt"));
        Assert.Contains("-p", args);
        Assert.Contains("haiku", args);
    }

    [Fact]
    public async Task Claude_code_missing_binary_is_explained()
    {
        var e = await Assert.ThrowsAsync<AiException>(() =>
            new ClaudeCodeClient(Path.Combine(dir, "nope.exe"), "", dir).CompleteAsync("x", new[] { U("y") }));
        Assert.Contains("нет файла", e.Message);
    }

    [Fact]
    public async Task OpenAi_compatible_request_and_reply()
    {
        int port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        JsonNode? received = null;
        string? auth = null;
        var server = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            auth = ctx.Request.Headers["Authorization"];
            using (var r = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) received = JsonNode.Parse(await r.ReadToEndAsync());
            var bytes = Encoding.UTF8.GetBytes("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Très bien !\"}}]}");
            ctx.Response.ContentType = "application/json";
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        });

        var client = new OpenAiCompatClient($"http://127.0.0.1:{port}/v1/", "key123", "test-model");
        string reply = await client.CompleteAsync("SYS", new[] { U("Je suis allé"), A("Bien"), U("Et toi ?") });
        await server;

        Assert.Equal("Très bien !", reply);
        Assert.Equal("Bearer key123", auth);
        Assert.Equal("test-model", received!["model"]!.GetValue<string>());
        var msgs = received["messages"]!.AsArray();
        Assert.Equal(new[] { "system", "user", "assistant", "user" }, msgs.Select(x => x!["role"]!.GetValue<string>()));
        Assert.Equal("SYS", msgs[0]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void OpenAi_reply_parsing_errors()
    {
        Assert.Throws<AiException>(() => OpenAiCompatClient.ParseReply("{\"choices\":[]}"));
        Assert.Throws<AiException>(() => OpenAiCompatClient.ParseReply("not json"));
    }

    static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
