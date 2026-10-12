using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

public class StorageTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "br-tests-" + Guid.NewGuid().ToString("N"));
    string F(string name) => Path.Combine(dir, name);

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public void Settings_roundtrip_including_texts()
    {
        var s = new AppSettings
        {
            IntervalMin = 42, AutoDay = true, AutoStartMin = 555, Popup = PopupMode.Fullscreen, Sound = "bell",
            TitleShort = "Эй!", BtnSnooze = "Через {N}", TipsShort = new[] { "один", "два" }
        };
        s.Save(dir, F("settings.ini"), F("tips.txt"), F("tips_long.txt"));
        var l = AppSettings.Load(F("settings.ini"), F("tips.txt"), F("tips_long.txt"));
        Assert.Equal(42, l.IntervalMin);
        Assert.True(l.AutoDay);
        Assert.Equal(555, l.AutoStartMin);
        Assert.Equal(PopupMode.Fullscreen, l.Popup);
        Assert.Equal("bell", l.Sound);
        Assert.Equal("Эй!", l.TitleShort);
        Assert.Equal("Через {N}", l.BtnSnooze);
        Assert.Equal(new[] { "один", "два" }, l.TipsShort);
        Assert.Equal(Texts.LongTips, l.TipsLong);
    }

    [Fact]
    public void Legacy_sound_keys_are_migrated()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(F("settings.ini"), "Sound=exclamation\nIntervalMin=999\n");
        var l = AppSettings.Load(F("settings.ini"), F("t"), F("tl"));
        Assert.Equal("system", l.Sound);
        Assert.Equal(240, l.IntervalMin); // зажато в допустимый диапазон
    }

    [Fact]
    public void History_appends_and_aggregates()
    {
        var h = new FileHistoryStore(F("history.csv"));
        h.Append(new DateTime(2026, 10, 12, 9, 0, 0), new DateTime(2026, 10, 12, 12, 0, 0), new DayStat { Minutes = 180, Done = 3 });
        h.Append(new DateTime(2026, 10, 12, 13, 0, 0), new DateTime(2026, 10, 12, 18, 0, 0), new DayStat { Minutes = 300, Skipped = 2 });
        var byDay = h.LoadByDay();
        var d = Assert.Single(byDay).Value;
        Assert.Equal((480, 3, 2), (d.Minutes, d.Done, d.Skipped));
        Assert.Equal(new DateTime(2026, 10, 12, 13, 0, 0), h.LastStart());
    }

    [Fact]
    public void Synth_produces_valid_wav()
    {
        foreach (var key in new[] { "chime", "soft", "bell" })
        {
            var wav = Synth.Melody(key, 0.7);
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
            Assert.True(wav.Length > 44 + 44100); // больше секунды звука
        }
    }
}
