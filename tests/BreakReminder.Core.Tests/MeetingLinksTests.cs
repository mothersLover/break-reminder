using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

public class MeetingLinksTests
{
    /// <summary>Одна встреча с заданными строками полей → найденная ссылка.</summary>
    static string? LinkOf(params string[] fields)
    {
        string ics = string.Join("\r\n", new[]
        {
            "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//test//EN",
            "BEGIN:VEVENT", "UID:x", "DTSTART:20261012T070000Z", "DTEND:20261012T080000Z", "SUMMARY:Встреча"
        }.Concat(fields).Concat(new[] { "END:VEVENT", "END:VCALENDAR" }));
        return Assert.Single(IcsParser.Parse(ics, new DateTime(2026, 10, 11), new DateTime(2026, 10, 14))).JoinUrl;
    }

    [Fact]
    public void Telemost_link_in_description_among_other_links()
    {
        Assert.Equal("https://telemost.yandex.ru/j/12345678901234",
            LinkOf(@"DESCRIPTION:Повестка: https://wiki.company.ru/page\nПодключиться: https://telemost.yandex.ru/j/12345678901234\nСпасибо!"));
    }

    [Fact]
    public void Zoom_in_location_with_trailing_punctuation()
    {
        Assert.Equal("https://us02web.zoom.us/j/87654321?pwd=abc",
            LinkOf("LOCATION:Zoom (https://us02web.zoom.us/j/87654321?pwd=abc)."));
    }

    [Fact]
    public void Google_meet_from_service_field()
    {
        Assert.Equal("https://meet.google.com/abc-defg-hij",
            LinkOf("X-GOOGLE-CONFERENCE:https://meet.google.com/abc-defg-hij", "DESCRIPTION:https://docs.google.com/document/d/1"));
    }

    [Fact]
    public void Teams_from_microsoft_field()
    {
        Assert.Equal("https://teams.microsoft.com/l/meetup-join/19%3ameeting_x",
            LinkOf("X-MICROSOFT-SKYPETEAMSMEETINGURL:https://teams.microsoft.com/l/meetup-join/19%3ameeting_x"));
    }

    [Fact]
    public void Unknown_service_taken_only_from_location_or_url_field()
    {
        Assert.Equal("https://call.example.org/room/42", LinkOf("LOCATION:https://call.example.org/room/42"));
        Assert.Equal("https://call.example.org/room/7", LinkOf("URL:https://call.example.org/room/7"));
        // ссылка только в описании и не на сервис звонков — не берём
        Assert.Null(LinkOf(@"DESCRIPTION:Материалы: https://wiki.company.ru/page"));
    }

    [Fact]
    public void Yandex_style_event_page_url_is_not_a_call()
    {
        // Яндекс кладёт в URL ссылку на страницу встречи в календаре — это не звонок
        Assert.Null(LinkOf("URL:https://calendar.yandex.ru/event?event_id=123"));
        // а звонок — в CONFERENCE
        Assert.Equal("https://telemost.360.yandex.ru/j/555",
            LinkOf("URL:https://calendar.yandex.ru/event?event_id=123", "CONFERENCE;VALUE=URI:https://telemost.360.yandex.ru/j/555"));
        // корпоративный сервис в «Месте» важнее ссылки на календарь
        Assert.Equal("https://ingobank.imcu.online/room/1",
            LinkOf("URL:https://calendar.yandex.ru/event?event_id=1", "LOCATION:https://ingobank.imcu.online/room/1"));
    }

    [Fact]
    public void No_link_and_non_http_ignored()
    {
        Assert.Null(LinkOf("LOCATION:Переговорная 5.12"));
        Assert.Null(LinkOf("LOCATION:file:///C:/Windows/system32/calc.exe"));
    }

    [Theory]
    [InlineData("https://telemost.yandex.ru/j/1", "Телемост")]
    [InlineData("https://company.zoom.us/j/1", "Zoom")]
    [InlineData("https://meet.google.com/a-b-c", "Google Meet")]
    [InlineData("https://call.example.org/x", "call.example.org")]
    public void Service_names(string url, string name) => Assert.Equal(name, MeetingLinks.ServiceName(url));
}
