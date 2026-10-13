using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace BreakReminder.Core;

/// <summary>Найденный населённый пункт.</summary>
public sealed record Place(string Name, string Region, string Country, double Lat, double Lon)
{
    public string Display => string.Join(", ", new[] { Name, Region, Country }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
}

/// <summary>Погода в конкретный час.</summary>
public sealed record WeatherHour(DateTime Time, double Temp, int Code, int PrecipProb);

/// <summary>Прогноз на сегодня.</summary>
public sealed record WeatherToday(DateTime Date, double Min, double Max, int PrecipProbMax, int DayCode, IReadOnlyList<WeatherHour> Hours)
{
    /// <summary>Погода в ближайший к моменту час.</summary>
    public WeatherHour? At(DateTime t) =>
        Hours.Where(h => h.Time <= t).MaxBy(h => h.Time) ?? Hours.MinBy(h => h.Time);

    /// <summary>Лучший час для прогулки в рабочее время (без осадков и потеплее), если такой есть.</summary>
    public WeatherHour? BestWalk(DateTime from, DateTime to) =>
        Hours.Where(h => h.Time >= from && h.Time <= to && h.PrecipProb <= 30 && !Weather.IsPrecipitation(h.Code))
             .OrderByDescending(h => h.Temp - h.PrecipProb / 10.0)
             .FirstOrDefault();
}

/// <summary>Погода от Open-Meteo (бесплатно, без ключа).</summary>
public static class Weather
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<List<Place>> SearchAsync(string name, CancellationToken ct = default)
    {
        var url = "https://geocoding-api.open-meteo.com/v1/search?count=5&language=ru&format=json&name=" + Uri.EscapeDataString(name.Trim());
        return ParsePlaces(await Get(url, ct).ConfigureAwait(false));
    }

    public static async Task<WeatherToday> TodayAsync(double lat, double lon, CancellationToken ct = default)
    {
        string inv(double v) => v.ToString(CultureInfo.InvariantCulture);
        var url = "https://api.open-meteo.com/v1/forecast?latitude=" + inv(lat) + "&longitude=" + inv(lon) +
                  "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max" +
                  "&hourly=temperature_2m,precipitation_probability,weather_code&timezone=auto&forecast_days=1";
        return ParseToday(await Get(url, ct).ConfigureAwait(false));
    }

    static async Task<string> Get(string url, CancellationToken ct)
    {
        try { return await Http.GetStringAsync(url, ct).ConfigureAwait(false); }
        catch (HttpRequestException) { throw new InvalidOperationException("Нет связи с сервисом погоды."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new InvalidOperationException("Сервис погоды не ответил вовремя."); }
    }

    public static List<Place> ParsePlaces(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<Place>();
        if (!doc.RootElement.TryGetProperty("results", out var arr)) return list;
        foreach (var r in arr.EnumerateArray())
            list.Add(new Place(
                r.GetProperty("name").GetString() ?? "",
                r.TryGetProperty("admin1", out var a) ? a.GetString() ?? "" : "",
                r.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "",
                r.GetProperty("latitude").GetDouble(),
                r.GetProperty("longitude").GetDouble()));
        return list;
    }

    /// <summary>Время в ответе — местное время самого места (timezone=auto); переводим в местное время компьютера.</summary>
    public static WeatherToday ParseToday(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        int offset = root.TryGetProperty("utc_offset_seconds", out var o) ? o.GetInt32() : 0;
        DateTime ToLocal(string s) =>
            new DateTimeOffset(DateTime.Parse(s, CultureInfo.InvariantCulture), TimeSpan.FromSeconds(offset)).LocalDateTime;

        var daily = root.GetProperty("daily");
        var hourly = root.GetProperty("hourly");
        var times = hourly.GetProperty("time").EnumerateArray().Select(t => t.GetString()!).ToArray();
        var temps = hourly.GetProperty("temperature_2m").EnumerateArray().ToArray();
        var probs = hourly.GetProperty("precipitation_probability").EnumerateArray().ToArray();
        var codes = hourly.GetProperty("weather_code").EnumerateArray().ToArray();
        var hours = new List<WeatherHour>();
        for (int i = 0; i < times.Length; i++)
        {
            if (temps[i].ValueKind != JsonValueKind.Number) continue;
            hours.Add(new WeatherHour(ToLocal(times[i]), temps[i].GetDouble(),
                codes[i].ValueKind == JsonValueKind.Number ? codes[i].GetInt32() : 0,
                probs[i].ValueKind == JsonValueKind.Number ? probs[i].GetInt32() : 0));
        }
        double First(string name) => daily.GetProperty(name)[0].ValueKind == JsonValueKind.Number ? daily.GetProperty(name)[0].GetDouble() : 0;
        return new WeatherToday(
            DateTime.Parse(daily.GetProperty("time")[0].GetString()!, CultureInfo.InvariantCulture),
            First("temperature_2m_min"), First("temperature_2m_max"),
            (int)First("precipitation_probability_max"), (int)First("weather_code"), hours);
    }

    // ---------- описание ----------

    /// <summary>Код погоды WMO → текст.</summary>
    public static string Describe(int code) => code switch
    {
        0 => "ясно",
        1 => "преимущественно ясно",
        2 => "переменная облачность",
        3 => "пасмурно",
        45 or 48 => "туман",
        51 or 53 or 55 => "морось",
        56 or 57 => "ледяная морось",
        61 => "небольшой дождь",
        63 => "дождь",
        65 => "сильный дождь",
        66 or 67 => "ледяной дождь",
        71 => "небольшой снег",
        73 => "снег",
        75 => "сильный снег",
        77 => "снежная крупа",
        80 or 81 => "ливень",
        82 => "сильный ливень",
        85 or 86 => "снегопад",
        95 => "гроза",
        96 or 99 => "гроза с градом",
        _ => "—"
    };

    public static bool IsPrecipitation(int code) => code >= 51;

    public static string Temp(double t)
    {
        int r = (int)Math.Round(t);
        return (r > 0 ? "+" : "") + r + "°";
    }

    /// <summary>«Сейчас +7°, пасмурно. Днём +4…+11°, осадки 20%.»</summary>
    public static string Summary(WeatherToday w, DateTime now)
    {
        var h = w.At(now);
        string current = h != null ? $"Сейчас {Temp(h.Temp)}, {Describe(h.Code)}. " : "";
        string rain = w.PrecipProbMax >= 20 ? $", вероятность осадков {w.PrecipProbMax}%" : ", без осадков";
        return current + $"Днём {Temp(w.Min)}…{Temp(w.Max)}{rain}.";
    }

    /// <summary>Строка для большого перерыва: выходить ли на улицу.</summary>
    public static string BreakLine(WeatherToday w, DateTime now)
    {
        var h = w.At(now);
        if (h == null) return "";
        return IsPrecipitation(h.Code) || h.PrecipProb >= 60
            ? $"На улице {Temp(h.Temp)}, {Describe(h.Code)} — пройдись лучше по офису."
            : h.Temp <= -15
                ? $"На улице {Temp(h.Temp)} — если выходить, то ненадолго."
                : $"На улице {Temp(h.Temp)}, {Describe(h.Code)} — хорошее время выйти.";
    }
}
