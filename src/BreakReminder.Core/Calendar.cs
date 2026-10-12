using System.Net.Http;
using Ical.Net.DataTypes;
using IcalCalendar = Ical.Net.Calendar;
using IcalEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace BreakReminder.Core;

/// <summary>Встреча из календаря (одно вхождение, уже в местном времени).</summary>
public sealed record Meeting(DateTime Start, DateTime End, string Title)
{
    public string Key => $"{Start:O}|{Title}";
    public string TimeRange => $"{Start:HH:mm}–{End:HH:mm}";
}

/// <summary>Разбор .ics: только «занятые» встречи на заданном отрезке времени.</summary>
public static class IcsParser
{
    public static List<Meeting> Parse(string ics, DateTime from, DateTime to)
    {
        var cal = IcalCalendar.Load(ics);
        var result = new List<Meeting>();
        foreach (var occ in cal.GetOccurrences(new CalDateTime(from), new CalDateTime(to)))
        {
            if (occ.Source is not IcalEvent ev) continue;
            if (ev.IsAllDay) continue;                                                     // дни рождения, отпуск и т.п.
            if (string.Equals(ev.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(ev.Transparency, "TRANSPARENT", StringComparison.OrdinalIgnoreCase)) continue; // «свободен»

            DateTime start = occ.Period.StartTime.AsSystemLocal;
            DateTime end = occ.Period.EndTime?.AsSystemLocal
                           ?? (occ.Period.Duration > TimeSpan.Zero ? start + occ.Period.Duration : start + ev.Duration);
            if (end <= start) continue;
            if (end <= from || start >= to) continue;
            result.Add(new Meeting(start, end, string.IsNullOrWhiteSpace(ev.Summary) ? "Встреча" : ev.Summary.Trim()));
        }
        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result.DistinctBy(m => m.Key).ToList();
    }
}

/// <summary>Загрузка календаря по секретной iCal-ссылке.</summary>
public static class CalendarFeed
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>webcal:// -> https://, пробелы по краям — прочь.</summary>
    public static string NormalizeUrl(string url)
    {
        url = (url ?? "").Trim();
        if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url[9..];
        return url;
    }

    /// <summary>Встречи со вчера по послезавтра. Бросает исключение с понятным текстом при ошибке.</summary>
    public static async Task<List<Meeting>> FetchAsync(string url, DateTime now, CancellationToken ct = default)
    {
        url = NormalizeUrl(url);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            throw new InvalidOperationException("Ссылка должна начинаться с https:// (или webcal://)");

        string text;
        try
        {
            // ConfigureAwait(false): разбор календаря идёт в фоне, а не в UI-потоке
            text = await Http.GetStringAsync(uri, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new InvalidOperationException(e.StatusCode is { } code
                ? $"Сервер календаря ответил ошибкой {(int)code}. Проверь ссылку."
                : "Нет связи с сервером календаря.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("Сервер календаря не ответил вовремя.");
        }

        if (!text.Contains("BEGIN:VCALENDAR"))
            throw new InvalidOperationException("По ссылке не календарь iCal — нужна ссылка именно в формате iCal/.ics.");
        try
        {
            return IcsParser.Parse(text, now.Date.AddDays(-1), now.Date.AddDays(3));
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("Не удалось разобрать календарь: " + e.Message);
        }
    }
}
