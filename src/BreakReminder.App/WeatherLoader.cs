using BreakReminder.Core;

namespace BreakReminder.App;

/// <summary>Погода на сегодня по настройкам; если координат нет — ищет город по названию.</summary>
public static class WeatherLoader
{
    public static async Task<(WeatherToday? Weather, string? Error)> LoadAsync(AppSettings s)
    {
        if (!s.Weather) return (null, null);
        try
        {
            double lat = s.WeatherLat, lon = s.WeatherLon;
            if (!s.HasWeatherPlace)
            {
                if (string.IsNullOrWhiteSpace(s.WeatherCity)) return (null, "Город не выбран — укажи его в настройках.");
                var place = (await Weather.SearchAsync(s.WeatherCity)).FirstOrDefault();
                if (place == null) return (null, $"Не нашёл «{s.WeatherCity}» — проверь название в настройках.");
                (lat, lon) = (place.Lat, place.Lon);
                // запоминаем найденное, чтобы не искать каждый раз
                s.WeatherLat = lat;
                s.WeatherLon = lon;
            }
            return (await Weather.TodayAsync(lat, lon), null);
        }
        catch (Exception e)
        {
            return (null, "Погоду узнать не удалось: " + e.Message);
        }
    }
}
