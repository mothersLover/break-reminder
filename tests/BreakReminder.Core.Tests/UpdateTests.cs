using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

public class UpdateTests
{
    [Theory]
    [InlineData("v4.1.0", "4.1.0")]
    [InlineData("4.1", "4.1.0")]
    [InlineData("v5", "5.0.0")]
    [InlineData("v4.1.2-beta", "4.1.2")]
    public void Parses_tags(string tag, string expected) => Assert.Equal(Version.Parse(expected), UpdateChecker.ParseVersion(tag));

    [Fact]
    public void Garbage_tag_is_null() => Assert.Null(UpdateChecker.ParseVersion("latest"));

    [Fact]
    public void Newer_version_compare()
    {
        Assert.True(UpdateChecker.ParseVersion("v4.1.0") > UpdateChecker.ParseVersion("v4.0.0"));
        Assert.True(UpdateChecker.ParseVersion("v4.0.10") > UpdateChecker.ParseVersion("v4.0.9"));   // не строковое сравнение
    }

    const string ReleaseJson = """
        {
          "tag_name": "v4.1.0",
          "html_url": "https://github.com/mothersLover/break-reminder/releases/tag/v4.1.0",
          "draft": false, "prerelease": false,
          "body": "### Добавлено\n- **Кнопка «Подключиться»** в окне встречи\n- поддержка `iMCU`, см. [README](https://x)\n\n## Скачать\n| Система | Файл |\n|---|---|\n| Windows | zip |",
          "assets": [
            { "name": "BreakReminder-win-x64.zip", "browser_download_url": "https://github.com/a/win.zip", "size": 45000000 },
            { "name": "SHA256SUMS.txt", "browser_download_url": "https://github.com/a/sums", "size": 400 }
          ]
        }
        """;

    [Fact]
    public void Parses_github_release()
    {
        var r = UpdateChecker.ParseRelease(ReleaseJson)!;
        Assert.Equal(new Version(4, 1, 0), r.Version);
        Assert.Equal("https://github.com/a/win.zip", r.Asset("BreakReminder-win-x64.zip")!.Url);
        Assert.NotNull(r.Asset(UpdateChecker.ChecksumsFile));
        Assert.Null(r.Asset("BreakReminder-linux-x64.tar.gz"));
        // описание: без markdown и без таблицы «Скачать»
        Assert.Equal("Добавлено:\n• Кнопка «Подключиться» в окне встречи\n• поддержка iMCU, см. README", r.Notes.Replace("\r", ""));
    }

    [Fact]
    public void Prerelease_is_ignored()
    {
        Assert.Null(UpdateChecker.ParseRelease(ReleaseJson.Replace("\"prerelease\": false", "\"prerelease\": true")));
    }

    [Fact]
    public void Asset_names_match_release_workflow()
    {
        Assert.Equal("BreakReminder-win-x64.zip", UpdateChecker.AssetNameFor("win-x64"));
        Assert.Equal("BreakReminder-linux-x64.tar.gz", UpdateChecker.AssetNameFor("linux-x64"));
        Assert.Equal("BreakReminder-osx-arm64.zip", UpdateChecker.AssetNameFor("osx-arm64"));
    }

    [Fact]
    public void Reads_sha256sums()
    {
        string sums = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  BreakReminder-linux-x64.tar.gz\n" +
                      "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB *BreakReminder-win-x64.zip\n";
        Assert.Equal(new string('b', 64), UpdateChecker.ExpectedSha256(sums, "BreakReminder-win-x64.zip"));
        Assert.Null(UpdateChecker.ExpectedSha256(sums, "BreakReminder-osx-x64.zip"));
    }
}

public class SnapshotTests
{
    [Fact]
    public void Day_survives_restart()
    {
        var t0 = new DateTime(2026, 10, 13, 9, 0, 0);
        var a = new Rig(t0);
        a.Engine.StartDay(a.Now);
        a.Advance(TimeSpan.FromMinutes(50.1));
        a.Engine.Answer(ReminderResult.Done, a.Now);
        a.Advance(TimeSpan.FromMinutes(20));
        var snap = a.Engine.Snapshot(a.Now)!;

        // «новый процесс» стартует через 10 секунд
        var b = new Rig(a.Now.AddSeconds(10));
        b.Engine.Restore(snap, "Продолжаем");
        b.Engine.Tick(b.Now);
        Assert.Equal(WorkState.Running, b.Engine.State);
        Assert.Equal(t0, b.Engine.DayStart);
        Assert.Equal(1, b.Engine.Done);
        Assert.InRange(b.Engine.Worked.TotalMinutes, 70.2, 70.4);    // 70 мин + 10 с перезапуска
        Assert.InRange(b.Engine.Remaining.TotalMinutes, 29.6, 29.9);
    }

    [Fact]
    public void Idle_has_no_snapshot()
    {
        var r = new Rig(new DateTime(2026, 10, 13, 9, 0, 0));
        Assert.Null(r.Engine.Snapshot(r.Now));
    }
}
