using System.Globalization;
using System.Text;

namespace BreakReminder.Core;

public sealed class DayStat
{
    public int Minutes, Done, Skipped, Snoozed;

    public void Add(DayStat o)
    {
        Minutes += o.Minutes;
        Done += o.Done;
        Skipped += o.Skipped;
        Snoozed += o.Snoozed;
    }
}

public interface IHistoryStore
{
    void Append(DateTime start, DateTime end, DayStat day);
    /// <summary>Когда начался последний записанный рабочий день (или DateTime.MinValue).</summary>
    DateTime LastStart();
    /// <summary>Все записи, сложенные по дням (за день может быть несколько сессий).</summary>
    Dictionary<DateTime, DayStat> LoadByDay();
}

/// <summary>history.csv — тот же формат, что у legacy-версии.</summary>
public sealed class FileHistoryStore : IHistoryStore
{
    const string Header = "Дата;Начало;Конец;Работа (мин);Сделано;Пропущено;Отложено";
    readonly string file;

    public FileHistoryStore() : this(Paths.HistoryFile) { }
    public FileHistoryStore(string file) { this.file = file; }

    public string FilePath => file;

    public void Append(DateTime start, DateTime end, DayStat d)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var sb = new StringBuilder();
            if (!File.Exists(file)) sb.AppendLine(Header);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd};{0:HH:mm};{1:HH:mm};{2};{3};{4};{5}",
                start, end, d.Minutes, d.Done, d.Skipped, d.Snoozed));
            File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }

    IEnumerable<string[]> Rows()
    {
        if (!File.Exists(file)) yield break;
        string[] lines;
        try { lines = File.ReadAllLines(file, Encoding.UTF8); }
        catch { yield break; }
        foreach (var line in lines)
        {
            var p = line.Split(';');
            if (p.Length >= 7) yield return p;
        }
    }

    public DateTime LastStart()
    {
        var last = DateTime.MinValue;
        foreach (var p in Rows())
            if (DateTime.TryParseExact(p[0] + " " + p[1], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt) && dt > last)
                last = dt;
        return last;
    }

    public Dictionary<DateTime, DayStat> LoadByDay()
    {
        var result = new Dictionary<DateTime, DayStat>();
        foreach (var p in Rows())
        {
            if (!DateTime.TryParseExact(p[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue; // заголовок или битая строка
            var d = new DayStat();
            int.TryParse(p[3], out d.Minutes);
            int.TryParse(p[4], out d.Done);
            int.TryParse(p[5], out d.Skipped);
            int.TryParse(p[6], out d.Snoozed);
            if (!result.TryGetValue(date, out var acc)) result[date] = acc = new DayStat();
            acc.Add(d);
        }
        return result;
    }
}
