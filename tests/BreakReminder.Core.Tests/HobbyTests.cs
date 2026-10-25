using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

public class HobbyTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "br-hobby-" + Guid.NewGuid().ToString("N"));
    static readonly DateTime Now = new(2026, 10, 26, 19, 0, 0);

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public void Existing_data_becomes_first_hobby_in_old_folder()
    {
        // до появления хобби: память и карточки лежат прямо в assistant/
        var old = new TutorMemory(dir);
        old.Profile = "Édito A2";
        new CardStore(Path.Combine(dir, "cards.json")).Add(new[] { ("я пошёл", "je suis allé", "") }, Now);

        var store = new HobbyStore(dir, "fr");
        Assert.Equal(HobbyStore.MainId, store.Active.Id);
        Assert.Equal("Французский", store.Active.Name);
        Assert.Equal("fr", store.Active.Lang);
        Assert.Equal(dir, store.DirFor(store.Active));

        var space = HobbySpace.Open(store, store.Active);
        Assert.Equal("Édito A2", space.Memory.Profile);                 // ничего не потерялось
        Assert.Single(space.Cards.All);
        Assert.Contains("про хобби «Французский»", space.Memory.SystemPrompt(new AppSettings(), Now));
    }

    [Fact]
    public void Each_hobby_has_its_own_memory_cards_goals()
    {
        var store = new HobbyStore(dir, "fr");
        var guitar = store.Add("Гитара", "🎸", "");
        Assert.Equal(Path.Combine(dir, "hobbies", guitar.Id), store.DirFor(guitar));

        var fr = HobbySpace.Open(store, store.Active);
        var gt = HobbySpace.Open(store, guitar);
        fr.Memory.Profile = "Édito A2";
        gt.Memory.Profile = "Учу аккорды, баррэ";
        gt.Cards.Add(new[] { ("аккорд Am", "x02210", "") }, Now);
        gt.Goals.Add("Сыграть «Звезду по имени Солнце»", Now.AddDays(30), Now);
        gt.Memory.Pin("Каждый день 10 минут", true, Now);

        Assert.Empty(fr.Cards.All);
        Assert.Empty(fr.Goals.All);
        Assert.Empty(fr.Memory.Pins);
        var reopened = HobbySpace.Open(new HobbyStore(dir, "fr"), guitar);
        Assert.Equal("Учу аккорды, баррэ", reopened.Memory.Profile);
        Assert.Single(reopened.Cards.All);
        Assert.Contains("Звезду по имени Солнце", reopened.Memory.SystemPrompt(new AppSettings(), Now));   // цели — в инструкциях
        Assert.Equal("Édito A2", HobbySpace.Open(store, store.All[0]).Memory.Profile);
    }

    [Fact]
    public void Switch_and_remove()
    {
        var store = new HobbyStore(dir, "fr");
        var guitar = store.Add("Гитара", "🎸", "");
        store.SetActive(guitar.Id);
        Assert.Equal(guitar.Id, new HobbyStore(dir, "fr").Active.Id);   // выбор сохраняется

        HobbySpace.Open(store, guitar).Memory.Profile = "аккорды";
        Assert.False(store.Remove(HobbyStore.MainId));                  // первое (старые данные) не удаляется
        Assert.True(store.Remove(guitar.Id));
        Assert.False(Directory.Exists(store.DirFor(guitar)));
        Assert.Equal(HobbyStore.MainId, store.Active.Id);
        Assert.Single(new HobbyStore(dir, "fr").All);
    }

    [Fact]
    public void Hobby_without_speech()
    {
        Assert.Equal("Без озвучки", SpeechText.Name(""));
        Assert.Contains(SpeechText.Languages, l => l.Code == "");
        Assert.Equal("Для этого хобби озвучка выключена.", SpeechText.InstallHint("Windows", ""));
        var store = new HobbyStore(dir, "");
        Assert.Equal("Учёба", store.Active.Name);
    }
}
