using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

public class PinTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "br-pins-" + Guid.NewGuid().ToString("N"));
    static readonly DateTime Now = new(2026, 10, 22, 20, 0, 0);

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public void Pin_unpin_persist_and_survive_new_conversation()
    {
        var m = new TutorMemory(dir);
        int changes = 0;
        m.PinsChanged += () => changes++;
        Assert.True(m.Pin("  Правило: aller, venir, partir — с être  ", false, Now));
        Assert.False(m.Pin("Правило: aller, venir, partir — с être", false, Now));   // уже закреплено
        Assert.False(m.Pin("   ", true, Now));
        Assert.True(m.Pin("План: каждый вечер 15 минут, Unité 5", true, Now.AddMinutes(1)));
        Assert.Equal(2, changes);

        m.Add(new ChatMessage(true, "привет", Now));
        m.ClearChat();                                                                   // «Новый разговор»
        var again = new TutorMemory(dir);
        Assert.Equal(2, again.Pins.Count);
        Assert.True(again.IsPinned("План: каждый вечер 15 минут, Unité 5"));
        Assert.False(again.Pins[0].FromUser);

        again.Unpin(again.Pins[0].Id);
        again.UnpinText("План: каждый вечер 15 минут, Unité 5");
        Assert.Empty(new TutorMemory(dir).Pins);
    }

    [Fact]
    public void Pins_go_to_assistant_newest_first_within_limit()
    {
        var m = new TutorMemory(dir);
        m.Pin("старое правило", false, Now);
        m.Pin("свежий план", true, Now.AddDays(1));
        string prompt = m.SystemPrompt(new AppSettings(), Now);
        Assert.Contains("<закреплено человеком", prompt);
        Assert.True(prompt.IndexOf("свежий план") < prompt.IndexOf("старое правило"));
        Assert.Contains("(человек): свежий план", prompt);
        Assert.Contains("(ты): старое правило", prompt);

        for (int i = 0; i < 10; i++) m.Pin(new string('я', 900) + i, false, Now.AddDays(2 + i));
        string big = m.SystemPrompt(new AppSettings(), Now);
        int start = big.IndexOf("<закреплено"), end = big.IndexOf("</закреплено>");
        Assert.True(end - start < TutorMemory.MaxPinnedChars + 300);                   // длинное не раздувает инструкции
        Assert.Contains(new string('я', 900) + "9", big);                               // самое новое на месте
    }

    [Fact]
    public void Limit_and_forget_all()
    {
        var m = new TutorMemory(dir);
        for (int i = 0; i < TutorMemory.MaxPins; i++) Assert.True(m.Pin("заметка " + i, true, Now));
        Assert.False(m.Pin("лишняя", true, Now));
        m.ForgetAll();
        Assert.Empty(new TutorMemory(dir).Pins);
        Assert.DoesNotContain("<закреплено", m.SystemPrompt(new AppSettings(), Now));
    }
}
