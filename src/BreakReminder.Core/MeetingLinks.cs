using System.Text.RegularExpressions;
using IcalEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace BreakReminder.Core;

/// <summary>
/// Ищет во встрече ссылку для подключения к звонку.
/// 1) ссылка на известный сервис видеозвонков — в любом поле (служебные X-…, URL, место, описание);
/// 2) иначе — любая ссылка из поля URL или «Место».
/// Случайные ссылки из описания (документы, вики) не берём, чтобы «Подключиться» не открывало что попало.
/// Только http(s).
/// </summary>
public static class MeetingLinks
{
    /// <summary>Хост (или хост + начало пути) сервисов видеозвонков.</summary>
    static readonly string[] Known =
    {
        "telemost.yandex.ru", "telemost.360.yandex.ru",
        "zoom.us", "zoom.com", "zoomgov.com",
        "teams.microsoft.com", "teams.live.com",
        "meet.google.com",
        "ktalk.ru",                                   // Контур.Толк (*.ktalk.ru)
        "salutejazz.ru", "jazz.sber.ru",              // SaluteJazz / Sber Jazz
        "mts-link.ru", "webinar.ru",                  // МТС Линк
        "dion.vc",
        "vk.com/call", "calls.vk.com",
        "webex.com",
        "whereby.com",
        "meet.jit.si",
        "gotomeeting.com", "meet.goto.com",
        "bluejeans.com",
        "chime.aws",
        "skype.com",
        "trueconf.ru", "trueconf.com",
        "vcs.mts.ru",
        "imcu.online"                                 // iMCU
    };

    /// <summary>Ссылки на сами календари (страница встречи) — это не звонок.</summary>
    static readonly string[] CalendarHosts =
    {
        "calendar.yandex.ru", "calendar.yandex.com", "calendar.360.yandex.ru",
        "calendar.google.com", "www.google.com/calendar",
        "outlook.live.com", "outlook.office.com", "outlook.office365.com",
        "icloud.com"
    };

    static readonly Regex Url = new(@"https?://[^\s<>""'\\]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? Find(IcalEvent ev)
    {
        // Поля для звонка идут первыми: CONFERENCE (стандарт, так делает Яндекс),
        // X-GOOGLE-CONFERENCE, X-MICROSOFT-SKYPETEAMSMEETINGURL и т.п.
        var conferenceFields = ev.Properties
            .Where(p => p.Name.Equals("CONFERENCE", StringComparison.OrdinalIgnoreCase)
                        || p.Name.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Value?.ToString());
        string? urlField = ev.Url?.ToString();
        string? location = ev.Location;
        string? description = ev.Description;

        var all = conferenceFields.Append(location).Append(urlField).Append(description);
        foreach (var url in all.SelectMany(Urls))
            if (IsKnown(url)) return url;

        // Неизвестный сервис: только из «Места» или URL, и не ссылка на сам календарь.
        foreach (var url in new[] { location, urlField }.SelectMany(Urls))
            if (!Matches(url, CalendarHosts)) return url;

        return null;
    }

    /// <summary>Все http(s)-ссылки из текста, без хвостовой пунктуации.</summary>
    internal static IEnumerable<string> Urls(string? text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        foreach (Match m in Url.Matches(text))
        {
            string u = m.Value.TrimEnd('.', ',', ';', ':', ')', ']', '}', '>', '!', '?');
            if (Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                yield return u;
        }
    }

    internal static bool IsKnown(string url) => Matches(url, Known);

    /// <summary>Хост ссылки совпадает с одним из списка (или его поддомен); элементы с «/» — хост + начало пути.</summary>
    static bool Matches(string url, string[] list)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        string host = uri.Host.ToLowerInvariant();
        string hostPath = host + uri.AbsolutePath.ToLowerInvariant();
        foreach (var k in list)
        {
            if (k.Contains('/'))
            {
                if (hostPath.StartsWith(k) || hostPath.StartsWith("www." + k)) return true;
            }
            else if (host == k || host.EndsWith("." + k))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Короткое название сервиса для подписи в окне («Телемост», «Zoom»…), или хост.</summary>
    public static string ServiceName(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        string h = uri.Host.ToLowerInvariant();
        if (h.Contains("telemost")) return "Телемост";
        if (h.Contains("zoom")) return "Zoom";
        if (h.Contains("teams.")) return "Microsoft Teams";
        if (h == "meet.google.com") return "Google Meet";
        if (h.EndsWith("ktalk.ru")) return "Контур.Толк";
        if (h.Contains("jazz")) return "SaluteJazz";
        if (h.Contains("mts-link") || h.Contains("webinar.ru")) return "МТС Линк";
        if (h.Contains("webex")) return "Webex";
        if (h.Contains("vk.com")) return "VK Звонки";
        if (h.EndsWith("imcu.online")) return "iMCU";
        return h.StartsWith("www.") ? h[4..] : h;
    }
}
