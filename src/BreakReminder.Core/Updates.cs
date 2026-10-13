using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BreakReminder.Core;

public sealed record ReleaseAsset(string Name, string Url, long Size);

/// <summary>Релиз на GitHub.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string Notes, string PageUrl, IReadOnlyList<ReleaseAsset> Assets)
{
    public ReleaseAsset? Asset(string name) =>
        Assets.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Проверка новых версий через GitHub Releases.</summary>
public static class UpdateChecker
{
    public const string Repo = "mothersLover/break-reminder";
    public const string ChecksumsFile = "SHA256SUMS.txt";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BreakReminder", Current.ToString()));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <summary>Версия запущенной программы (при сборке релиза берётся из тега).</summary>
    public static Version Current => Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    /// <summary>"v4.1.0", "4.1", "v4.1.0-beta" → 4.1.0; мусор → null.</summary>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var m = Regex.Match(tag.Trim(), @"^[vV]?(\d+)(?:\.(\d+))?(?:\.(\d+))?");
        if (!m.Success) return null;
        int Part(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value) : 0;
        return new Version(Part(1), Part(2), Part(3));
    }

    /// <summary>Разбор ответа GET /repos/{repo}/releases/latest.</summary>
    public static ReleaseInfo? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = ParseVersion(tag);
        if (version == null) return null;
        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var arr))
            foreach (var a in arr.EnumerateArray())
                assets.Add(new ReleaseAsset(
                    a.GetProperty("name").GetString() ?? "",
                    a.GetProperty("browser_download_url").GetString() ?? "",
                    a.TryGetProperty("size", out var s) ? s.GetInt64() : 0));
        string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        string page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : $"https://github.com/{Repo}/releases";
        return new ReleaseInfo(version, tag, CleanNotes(notes), page, assets);
    }

    /// <summary>Последний релиз, или null, если релизов нет. Бросает исключение с понятным текстом при ошибке сети.</summary>
    public static async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        // для разработки и тестов: свой адрес вместо GitHub (JSON в формате GitHub API)
        string url = Environment.GetEnvironmentVariable("BREAKREMINDER_UPDATE_FEED") is { Length: > 0 } feed
            ? feed
            : $"https://api.github.com/repos/{Repo}/releases/latest";
        HttpResponseMessage resp;
        try
        {
            resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException) { throw new InvalidOperationException("Нет связи с GitHub."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new InvalidOperationException("GitHub не ответил вовремя."); }

        using (resp)
        {
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            if ((int)resp.StatusCode == 403) throw new InvalidOperationException("GitHub временно ограничил проверки — попробуй позже.");
            if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"GitHub ответил ошибкой {(int)resp.StatusCode}.");
            return ParseRelease(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
    }

    /// <summary>Идентификатор сборки для этой машины: win-x64, linux-x64, osx-arm64…</summary>
    public static string CurrentRid
    {
        get
        {
            string arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X86 => "x86",
                _ => "x64"
            };
            string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
            return os + "-" + arch;
        }
    }

    /// <summary>Имя архива сборки в релизе (как его называет release.yml).</summary>
    public static string AssetNameFor(string rid) =>
        rid.StartsWith("linux") ? $"BreakReminder-{rid}.tar.gz" : $"BreakReminder-{rid}.zip";

    /// <summary>Хэш файла из SHA256SUMS.txt (формат sha256sum: "hash  имя" или "hash *имя").</summary>
    public static string? ExpectedSha256(string sums, string fileName)
    {
        foreach (var line in sums.Split('\n'))
        {
            var m = Regex.Match(line.Trim(), @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
            if (m.Success && m.Groups[2].Value.Trim() == fileName) return m.Groups[1].Value.ToLowerInvariant();
        }
        return null;
    }

    /// <summary>Описание релиза (markdown) → простой текст для окна. Таблица «Скачать» отрезается.</summary>
    public static string CleanNotes(string markdown)
    {
        var sb = new StringBuilder();
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (Regex.IsMatch(line, @"^#{1,6}\s*Скачать")) break;      // дальше — таблица файлов
            if (line.StartsWith('|')) continue;
            line = Regex.Replace(line, @"^#{1,6}\s*(.+)$", "$1:");      // заголовки → «Добавлено:»
            line = Regex.Replace(line, @"^(\s*)[-*]\s+", "$1• ");      // пункты списка
            line = line.Replace("**", "").Replace("`", "");
            line = Regex.Replace(line, @"\[([^\]]+)\]\([^)]+\)", "$1"); // [текст](ссылка) → текст
            sb.AppendLine(line);
        }
        return Regex.Replace(sb.ToString(), @"\n{3,}", "\n\n").Trim();
    }
}
