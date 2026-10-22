using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

public class SpeechTests : IDisposable
{
    readonly string file = Path.Combine(Path.GetTempPath(), "br-speech-" + Guid.NewGuid().ToString("N") + ".json");
    static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0);

    public void Dispose()
    {
        try { File.Delete(file); } catch { }
    }

    [Fact]
    public void Only_foreign_phrases_are_spoken()
    {
        var parts = SpeechText.Foreign("Почти! С aller нужен être, а не avoir. Попробуй ещё раз: «Je suis allé au cinéma».");
        Assert.Equal(new[] { "Je suis allé au cinéma." }, parts);   // одиночные слова посреди русского — нет

        var list = SpeechText.Foreign("Исправленные предложения:\n1. Hier, je suis allé au cinéma.\n2. Elle est venue à la maison.\nУчебник Édito A2, Unité 5.");
        Assert.Equal(new[] { "Hier, je suis allé au cinéma.", "Elle est venue à la maison.", "Édito A2, Unité 5." }, list);   // название по-французски — тоже можно

        Assert.Equal(new[] { "allée." }, SpeechText.Foreign("Для женского рода — «allée»."));   // в кавычках — да
        Assert.Empty(SpeechText.Foreign("Отлично, всё верно! Продолжаем."));
        Assert.Equal(new[] { "Bonsoir ! Ça va ?" }, SpeechText.Foreign("Bonsoir ! Ça va ?"));
    }

    [Fact]
    public void Pause_grows_with_phrase_but_is_capped()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(1500 + 70 * 5), SpeechText.RepeatPause("allée"));
        Assert.Equal(TimeSpan.FromSeconds(8), SpeechText.RepeatPause(new string('a', 500)));
    }

    [Fact]
    public void Listening_picks_due_then_forgotten_then_new_and_skips_russian_answers()
    {
        var s = new CardStore(file);
        s.Add(new[]
        {
            ("старая", "ancienne", ""), ("забытая", "oubliée", ""), ("новая", "nouvelle", ""),
            ("перевод на русский", "по-русски ответ", "")
        }, Now.AddDays(-10));
        var old = s.All[0];
        var forgotten = s.All[1];
        s.Review(old, Grade.Good, Now.AddDays(-10));                   // срок вышел
        s.Review(forgotten, Grade.Again, Now.AddDays(-1));
        s.Review(forgotten, Grade.Good, Now.AddDays(-1));              // забывал, но срок ещё не вышел

        var list = s.ForListening(Now, 10);
        Assert.Equal(new[] { "ancienne", "oubliée", "nouvelle" }, list.Select(c => c.Back));
        Assert.Equal(2, s.ForListening(Now, 2).Count);
    }

    [Fact]
    public void Speech_settings_roundtrip_and_hints()
    {
        string dir = Path.Combine(Path.GetTempPath(), "br-sp-" + Guid.NewGuid().ToString("N"));
        try
        {
            var st = new AppSettings { SpeechLang = "de", SpeechSlow = false, ListenInBreak = false, ListenCount = 8, SpeechAutoCards = false };
            string f(string n) => Path.Combine(dir, n);
            st.Save(dir, f("s.ini"), f("t.txt"), f("tl.txt"));
            var l = AppSettings.Load(f("s.ini"), f("t.txt"), f("tl.txt"));
            Assert.Equal("de", l.SpeechLang);
            Assert.False(l.SpeechSlow);
            Assert.False(l.ListenInBreak);
            Assert.False(l.SpeechAutoCards);
            Assert.Equal(8, l.ListenCount);
            Assert.Equal("fr", new AppSettings().SpeechLang);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        Assert.Contains("Добавить голоса", SpeechText.InstallHint("Windows", "fr"));
        Assert.Contains("французский", SpeechText.InstallHint("Windows", "fr"));
        Assert.Contains("espeak-ng", SpeechText.InstallHint("Linux", "fr"));
        Assert.Equal("Французский", SpeechText.Name("fr"));
    }
}
