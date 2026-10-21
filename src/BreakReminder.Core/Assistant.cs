using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;

namespace BreakReminder.Core;

/// <summary>Через что работает ИИ-помощник.</summary>
public enum AiProvider { ClaudeCode, Anthropic, OpenAi, Ollama }

/// <summary>
/// Сообщение в разговоре с помощником. Local — служебное (приветствие, ошибка): модели не отправляется.
/// Images — пути к приложенным фото (страница учебника, тетрадь).
/// </summary>
public sealed record ChatMessage(bool IsUser, string Text, DateTime At, bool Local = false, IReadOnlyList<string>? Images = null)
{
    public bool HasImages => Images is { Count: > 0 };
}

/// <summary>Фото для помощника: тип файла и содержимое в base64.</summary>
public static class AiImages
{
    public static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    public static string MediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/png"
    };

    public static string Base64(string path)
    {
        try { return Convert.ToBase64String(File.ReadAllBytes(path)); }
        catch (Exception e) { throw new AiException($"Не получилось прочитать фото {Path.GetFileName(path)}: {e.Message}"); }
    }

    /// <summary>Подпись вместо фото — там, где картинку не передать (заметки, старые сообщения).</summary>
    public static string Mark(ChatMessage m) => m.HasImages ? $"[приложено фото: {m.Images!.Count}] " : "";
}

/// <summary>Понятная человеку ошибка ИИ (нет ключа, нет входа, сервис не ответил…).</summary>
public sealed class AiException(string message) : Exception(message);

/// <summary>Один запрос к модели: инструкции + разговор → ответ.</summary>
public interface IAiClient
{
    Task<string> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, CancellationToken ct = default);
}

public static class AiClients
{
    public static readonly string[] ProviderNames =
    {
        "Claude Code (по подписке Claude)",
        "Anthropic API (свой ключ)",
        "OpenAI-совместимый сервис",
        "Ollama (локально, бесплатно)"
    };

    public const string DefaultAnthropicModel = "claude-opus-5-5";
    public const string DefaultOpenAiUrl = "https://api.openai.com/v1";
    public const string DefaultOllamaUrl = "http://localhost:11434/v1";

    /// <summary>Клиент по настройкам. Бросает AiException, если чего-то не хватает.</summary>
    public static IAiClient Create(AppSettings s) => s.AiProvider switch
    {
        AiProvider.ClaudeCode => new ClaudeCodeClient(s.AiClaudePath, s.AiClaudeModel),
        AiProvider.Anthropic => new AnthropicAiClient(Need(s.AiAnthropicKey, "Укажи API-ключ Anthropic."),
            Or(s.AiAnthropicModel, DefaultAnthropicModel)),
        AiProvider.OpenAi => new OpenAiCompatClient(Or(s.AiOpenAiUrl, DefaultOpenAiUrl), Need(s.AiOpenAiKey, "Укажи API-ключ."),
            Need(s.AiOpenAiModel, "Укажи модель.")),
        _ => new OpenAiCompatClient(Or(s.AiOllamaUrl, DefaultOllamaUrl), "", Need(s.AiOllamaModel, "Укажи модель Ollama (например, llama3.2)."),
            ollama: true)
    };

    /// <summary>Короткое описание подключения для заголовка окна помощника.</summary>
    public static string Describe(AppSettings s) => s.AiProvider switch
    {
        AiProvider.ClaudeCode => "Claude Code" + (s.AiClaudeModel.Length > 0 ? " · " + s.AiClaudeModel : "") + " · по подписке",
        AiProvider.Anthropic => "Anthropic API · " + Or(s.AiAnthropicModel, DefaultAnthropicModel),
        AiProvider.OpenAi => s.AiOpenAiModel + " · " + Host(Or(s.AiOpenAiUrl, DefaultOpenAiUrl)),
        _ => "Ollama · " + s.AiOllamaModel
    };

    /// <summary>
    /// Сообщения для модели: без служебных, начиная с сообщения человека, подряд идущие от одного автора склеены
    /// (после ошибки человек мог написать ещё раз), не больше max последних. Фото отправляются только из
    /// keepImages последних сообщений с фото — старые заменяются пометкой, чтобы не платить за них снова и снова.
    /// </summary>
    public static List<ChatMessage> ForModel(IEnumerable<ChatMessage> chat, int max = 40, int keepImages = 2)
    {
        var list = new List<ChatMessage>();
        foreach (var m in chat.Where(m => !m.Local && (m.Text.Trim().Length > 0 || m.HasImages)))
        {
            if (list.Count > 0 && list[^1].IsUser == m.IsUser)
            {
                var prev = list[^1];
                var images = (prev.Images ?? Array.Empty<string>()).Concat(m.Images ?? Array.Empty<string>()).ToList();
                list[^1] = prev with { Text = (prev.Text + "\n\n" + m.Text).Trim(), At = m.At, Images = images.Count > 0 ? images : null };
            }
            else
                list.Add(m);
        }
        if (list.Count > max) list = list.GetRange(list.Count - max, max);
        while (list.Count > 0 && !list[0].IsUser) list.RemoveAt(0);

        int seen = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (!list[i].HasImages) continue;
            if (++seen > keepImages)
                list[i] = list[i] with { Text = (AiImages.Mark(list[i]) + list[i].Text).Trim(), Images = null };
        }
        return list;
    }

    static string Need(string v, string error) => string.IsNullOrWhiteSpace(v) ? throw new AiException(error) : v.Trim();
    static string Or(string v, string def) => string.IsNullOrWhiteSpace(v) ? def : v.Trim();
    static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;
}

/// <summary>Claude через Anthropic API (официальный SDK).</summary>
public sealed class AnthropicAiClient(string apiKey, string model, string? baseUrl = null) : IAiClient
{
    public async Task<string> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
    {
        // baseUrl — только для тестов (поддельный сервер)
        var client = baseUrl == null ? new AnthropicClient { ApiKey = apiKey } : new AnthropicClient { ApiKey = apiKey, BaseUrl = baseUrl };
        try
        {
            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model = model,
                MaxTokens = 16000,
                System = system,
                Messages = messages.Select(ToParam).ToList()
            }, ct).ConfigureAwait(false);
            if (response.StopReason == "refusal") throw new AiException("Модель отказалась отвечать на это сообщение.");
            var text = string.Join("\n", response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();
            return text.Length > 0 ? text : throw new AiException("Модель вернула пустой ответ.");
        }
        catch (AnthropicUnauthorizedException) { throw new AiException("Ключ Anthropic не подходит — проверь его в настройках."); }
        catch (AnthropicForbiddenException) { throw new AiException("У ключа нет доступа к этой модели."); }
        catch (AnthropicNotFoundException) { throw new AiException($"Модель «{model}» не найдена — проверь название."); }
        catch (AnthropicRateLimitException) { throw new AiException("Слишком много запросов или закончился баланс — попробуй позже."); }
        catch (Anthropic5xxException) { throw new AiException("Сервис Anthropic сейчас недоступен — попробуй позже."); }
        catch (AnthropicIOException) { throw new AiException("Нет связи с Anthropic."); }
        catch (AnthropicApiException e) { throw new AiException("Anthropic ответил ошибкой: " + e.Message); }
    }

    /// <summary>Сообщение для API: с фото — фото первыми, потом текст (так модель лучше их читает).</summary>
    static MessageParam ToParam(ChatMessage m)
    {
        var role = m.IsUser ? Role.User : Role.Assistant;
        if (!m.HasImages) return new MessageParam { Role = role, Content = m.Text };
        var blocks = new List<ContentBlockParam>();
        foreach (var path in m.Images!)
            blocks.Add(new ImageBlockParam { Source = new Base64ImageSource { Data = AiImages.Base64(path), MediaType = AiImages.MediaType(path) } });
        blocks.Add(new TextBlockParam { Text = m.Text.Length > 0 ? m.Text : "Посмотри фото." });
        return new MessageParam { Role = role, Content = blocks };
    }
}

/// <summary>Любой сервис с OpenAI-совместимым API (/chat/completions): OpenAI, OpenRouter, DeepSeek, Ollama, LM Studio…</summary>
public sealed class OpenAiCompatClient(string baseUrl, string apiKey, string model, bool ollama = false) : IAiClient
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public async Task<string> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
    {
        var msgs = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = system } };
        foreach (var m in messages) msgs.Add(new JsonObject { ["role"] = m.IsUser ? "user" : "assistant", ["content"] = Content(m) });
        var body = new JsonObject { ["model"] = model, ["messages"] = msgs };

        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        if (apiKey.Length > 0) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw new AiException(ollama ? "Ollama не отвечает — она запущена?" : "Нет связи с сервисом.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new AiException("Сервис не ответил вовремя."); }

        using (resp)
        {
            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                if ((int)resp.StatusCode == 401) throw new AiException("Ключ не подходит — проверь его в настройках.");
                if ((int)resp.StatusCode == 404 && ollama) throw new AiException($"Модель «{model}» не найдена. Скачай её: ollama pull {model}");
                throw new AiException($"Сервис ответил ошибкой {(int)resp.StatusCode}" + (ErrorText(json) is { } e ? ": " + e : "."));
            }
            return ParseReply(json);
        }
    }

    /// <summary>Содержимое сообщения: строка или (с фото) массив частей text + image_url с data:-ссылкой.</summary>
    static JsonNode Content(ChatMessage m)
    {
        if (!m.HasImages) return JsonValue.Create(m.Text)!;
        var parts = new JsonArray();
        foreach (var path in m.Images!)
            parts.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject { ["url"] = $"data:{AiImages.MediaType(path)};base64,{AiImages.Base64(path)}" }
            });
        parts.Add(new JsonObject { ["type"] = "text", ["text"] = m.Text.Length > 0 ? m.Text : "Посмотри фото." });
        return parts;
    }

    /// <summary>Текст ответа из choices[0].message.content.</summary>
    public static string ParseReply(string json)
    {
        try
        {
            var text = JsonNode.Parse(json)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()?.Trim();
            if (!string.IsNullOrEmpty(text)) return text;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentOutOfRangeException) { }
        throw new AiException("Сервис вернул пустой или непонятный ответ.");
    }

    static string? ErrorText(string json)
    {
        try { return JsonNode.Parse(json)?["error"]?["message"]?.GetValue<string>(); }
        catch { return null; }
    }
}

/// <summary>
/// Claude через установленный Claude Code (`claude -p`): работает по подписке, без API-ключа.
/// Каждый запрос — отдельный запуск без инструментов и без сохранения сессии; разговор передаётся текстом.
/// </summary>
public sealed class ClaudeCodeClient(string customPath, string model, string? workDir = null) : IAiClient
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    /// <summary>Где искать claude: свой путь из настроек, PATH, стандартные места установки.</summary>
    public static string? Locate(string customPath = "")
    {
        if (!string.IsNullOrWhiteSpace(customPath)) return File.Exists(customPath.Trim()) ? customPath.Trim() : null;

        bool win = OperatingSystem.IsWindows();
        string[] names = win ? new[] { "claude.exe", "claude.cmd" } : new[] { "claude" };
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        dirs.Add(Path.Combine(home, ".local", "bin"));
        dirs.Add(Path.Combine(home, ".claude", "local"));
        if (win) dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));
        else dirs.AddRange(new[] { "/opt/homebrew/bin", "/usr/local/bin", Path.Combine(home, ".npm-global", "bin") });

        foreach (var dir in dirs)
            foreach (var name in names)
            {
                try
                {
                    var p = Path.Combine(dir.Trim('"'), name);
                    if (File.Exists(p)) return p;
                }
                catch (ArgumentException) { }
            }
        return null;
    }

    public async Task<string> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
    {
        string exe = Locate(customPath) ?? throw new AiException(string.IsNullOrWhiteSpace(customPath)
            ? "Claude Code не найден. Установи его (claude.com/claude-code) и войди своим аккаунтом, или укажи путь к claude в настройках."
            : "По указанному пути нет файла claude.");

        // Рабочая папка — пустая, чтобы Claude Code не подхватывал чужие проектные файлы.
        string work = workDir ?? Path.Combine(Paths.DataDir, "assistant", "claude-code");
        Directory.CreateDirectory(work);

        var psi = new ProcessStartInfo
        {
            WorkingDirectory = work,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            psi.ArgumentList.Add("/c");
        }
        else psi.FileName = exe;
        if (psi.FileName != exe) psi.ArgumentList.Add(exe);
        var imageDirs = messages.Where(m => m.HasImages).SelectMany(m => m.Images!).Select(Path.GetDirectoryName).OfType<string>().Distinct().ToList();
        foreach (var a in Arguments(model, imageDirs)) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        try { p.Start(); }
        catch (Exception e) { throw new AiException("Не удалось запустить Claude Code: " + e.Message); }

        try
        {
            // инструкции и разговор — через stdin: без ограничений длины командной строки и проблем с переводами строк
            await p.StandardInput.WriteAsync(BuildPrompt(system, messages).AsMemory(), ct).ConfigureAwait(false);
            p.StandardInput.Close();
            var stdout = p.StandardOutput.ReadToEndAsync(ct);
            var stderr = p.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            try { await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AiException("Claude Code не ответил вовремя."); }
            return ParseResult(await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        finally
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
        }
    }

    /// <summary>
    /// Аргументы запуска: один ответ в JSON, без сохранения сессии. Инструментов нет; если есть фото —
    /// только чтение файлов (Read) и только в папках с фото.
    /// </summary>
    public static List<string> Arguments(string model, IReadOnlyList<string>? imageDirs = null)
    {
        bool images = imageDirs is { Count: > 0 };
        var args = new List<string>
        {
            "-p", "--output-format", "json", "--tools", images ? "Read" : "", "--no-session-persistence",
            "--system-prompt", "Ты — помощник по учёбе, хобби и привычкам. Следуй инструкциям в сообщении."
        };
        if (images)
        {
            args.Add("--allowedTools");
            args.Add("Read");
            foreach (var d in imageDirs!) { args.Add("--add-dir"); args.Add(d); }
        }
        if (!string.IsNullOrWhiteSpace(model)) { args.Add("--model"); args.Add(model.Trim()); }
        return args;
    }

    /// <summary>Инструкции и разговор одним текстом (Claude Code принимает одно сообщение).</summary>
    public static string BuildPrompt(string system, IReadOnlyList<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<инструкции>").AppendLine(system.Trim()).AppendLine("</инструкции>").AppendLine();
        if (messages.Count > 1)
        {
            sb.AppendLine("<разговор>");
            foreach (var m in messages.Take(messages.Count - 1))
                sb.Append(m.IsUser ? "Человек: " : "Помощник: ").Append(ImageNote(m)).AppendLine(m.Text.Trim()).AppendLine();
            sb.AppendLine("</разговор>").AppendLine();
        }
        sb.AppendLine("Ответь на сообщение человека (только текст ответа, без пометки «Помощник:»):");
        if (messages.Count > 0) sb.Append(ImageNote(messages[^1])).Append(messages[^1].Text.Trim());
        return sb.ToString();
    }

    /// <summary>Фото — путями к файлам: Claude Code откроет их инструментом Read.</summary>
    static string ImageNote(ChatMessage m) =>
        m.HasImages ? "[приложено фото — открой и посмотри: " + string.Join(", ", m.Images!) + "]\n" : "";

    /// <summary>Разбор вывода `claude -p --output-format json`.</summary>
    public static string ParseResult(string stdout, string stderr = "")
    {
        JsonNode? root = null;
        try { root = JsonNode.Parse(stdout.Trim()); }
        catch (JsonException) { }
        if (root == null)
            throw new AiException("Claude Code ответил непонятно" + (stderr.Trim() is { Length: > 0 } e ? ": " + Short(e) : "."));

        string result = (root["result"]?.GetValue<string>() ?? "").Trim();
        bool error = root["is_error"]?.GetValue<bool>() ?? false;
        if (error)
        {
            if (result.Contains("Not logged in", StringComparison.OrdinalIgnoreCase) || result.Contains("/login"))
                throw new AiException("Claude Code не залогинен. Открой терминал, выполни claude и войди своим аккаунтом (/login).");
            if (result.Contains("limit", StringComparison.OrdinalIgnoreCase))
                throw new AiException("Лимит подписки на сейчас исчерпан: " + Short(result));
            throw new AiException("Claude Code: " + (result.Length > 0 ? Short(result) : "ошибка без описания."));
        }
        return result.Length > 0 ? result : throw new AiException("Claude Code вернул пустой ответ.");
    }

    static string Short(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
