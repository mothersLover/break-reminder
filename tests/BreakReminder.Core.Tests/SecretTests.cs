using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

[Collection("AiClients.Gate")]   // AppSettings.Protector — общий (static), как и AiClients.Gate
public class SecretTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "br-secret-" + Guid.NewGuid().ToString("N"));
    string F(string n) => Path.Combine(dir, n);

    /// <summary>Игрушечное «шифрование»: base64 задом наперёд. Unprotect не своего — null.</summary>
    sealed class FakeProtector : ISecretProtector
    {
        public string Protect(string plain) => new string(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plain)).Reverse().ToArray());
        public string? Unprotect(string stored)
        {
            try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(new string(stored.Reverse().ToArray()))); }
            catch { return null; }
        }
    }

    public void Dispose()
    {
        AppSettings.Protector = null;
        try { Directory.Delete(dir, true); } catch { }
    }

    AppSettings Roundtrip(AppSettings s)
    {
        s.Save(dir, F("settings.ini"), F("t.txt"), F("tl.txt"));
        return AppSettings.Load(F("settings.ini"), F("t.txt"), F("tl.txt"));
    }

    [Fact]
    public void Secrets_are_encrypted_in_file_and_read_back()
    {
        AppSettings.Protector = new FakeProtector();
        var l = Roundtrip(new AppSettings
        {
            AiAnthropicKey = "sk-ant-SECRET", AiOpenAiKey = "sk-openai-SECRET",
            CalendarUrl = "https://calendar.google.com/calendar/ical/private-SECRET/basic.ics", AiOpenAiModel = "gpt"
        });
        Assert.Equal("sk-ant-SECRET", l.AiAnthropicKey);
        Assert.Equal("sk-openai-SECRET", l.AiOpenAiKey);
        Assert.Equal("https://calendar.google.com/calendar/ical/private-SECRET/basic.ics", l.CalendarUrl);

        string file = File.ReadAllText(F("settings.ini"));
        Assert.DoesNotContain("SECRET", file);                       // ни ключей, ни ссылки открытым текстом
        Assert.Contains("AiAnthropicKey=enc:", file);
        Assert.Contains("AiOpenAiModel=gpt", file);                  // остальное — как было
        Assert.Contains("AiOpenAiKey=enc:", file);
    }

    [Fact]
    public void Old_plain_values_are_read_and_encrypted_on_next_save()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(F("settings.ini"), "AiAnthropicKey=sk-old-plain\nCalendarUrl=https://x/old.ics\n");
        AppSettings.Protector = new FakeProtector();
        var l = AppSettings.Load(F("settings.ini"), F("t.txt"), F("tl.txt"));
        Assert.Equal("sk-old-plain", l.AiAnthropicKey);
        Assert.Equal("https://x/old.ics", l.CalendarUrl);
        l.Save(dir, F("settings.ini"), F("t.txt"), F("tl.txt"));
        Assert.DoesNotContain("sk-old-plain", File.ReadAllText(F("settings.ini")));
    }

    [Fact]
    public void Undecryptable_secret_becomes_empty_not_garbage()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(F("settings.ini"), "AiAnthropicKey=enc:@@@не-наше@@@\nAiOn=1\n");
        AppSettings.Protector = new FakeProtector();
        var l = AppSettings.Load(F("settings.ini"), F("t.txt"), F("tl.txt"));
        Assert.Equal("", l.AiAnthropicKey);                          // с другого компьютера — ввести заново
        Assert.True(l.AiOn);

        AppSettings.Protector = null;                                // без шифровальщика «enc:» тоже не подсовываем как ключ
        Assert.Equal("", AppSettings.Load(F("settings.ini"), F("t.txt"), F("tl.txt")).AiAnthropicKey);
    }

    [Fact]
    public void Without_protector_values_stay_plain()
    {
        AppSettings.Protector = null;
        var l = Roundtrip(new AppSettings { AiOpenAiKey = "plain-key" });
        Assert.Equal("plain-key", l.AiOpenAiKey);
        Assert.Contains("AiOpenAiKey=plain-key", File.ReadAllText(F("settings.ini")));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(F("settings.ini")));
    }
}
