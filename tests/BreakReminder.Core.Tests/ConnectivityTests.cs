using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

[Collection("AiClients.Gate")]   // AiClients.Gate — общий (static): эти тесты не должны идти параллельно с AiClientTests
public class ConnectivityTests
{
    DateTime now = new(2026, 10, 23, 10, 0, 0);

    sealed class CountingClient : IAiClient
    {
        public int Calls;
        public Task<string> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult("ответ");
        }
    }

    [Fact]
    public async Task Online_result_is_cached_offline_is_rechecked_sooner()
    {
        int probes = 0;
        bool weatherUp = true;
        var gate = new ConnectivityGate(_ => { probes++; return weatherUp ? Task.CompletedTask : throw new HttpRequestException("timeout"); }, () => now);
        int changes = 0;
        gate.Changed += () => changes++;

        Assert.Null(gate.Online);
        Assert.True(await gate.CheckAsync());
        now += TimeSpan.FromMinutes(9);
        Assert.True(await gate.CheckAsync());
        Assert.Equal(1, probes);                                    // 10 минут — без новых запросов прогноза

        weatherUp = false;
        now += TimeSpan.FromMinutes(2);
        Assert.False(await gate.CheckAsync());
        Assert.Equal("timeout", gate.Error);
        Assert.Equal(2, probes);
        now += TimeSpan.FromSeconds(50);
        Assert.False(await gate.CheckAsync());
        Assert.Equal(2, probes);                                    // «нет связи» помним минуту
        now += TimeSpan.FromSeconds(20);
        weatherUp = true;
        Assert.True(await gate.CheckAsync());
        Assert.Equal(3, probes);
        Assert.Equal(3, changes);                                   // null→да, да→нет, нет→да

        Assert.True(await gate.CheckAsync(force: true));
        Assert.Equal(4, probes);
    }

    [Fact]
    public async Task Concurrent_checks_share_one_weather_request()
    {
        int probes = 0;
        var release = new TaskCompletionSource();
        var gate = new ConnectivityGate(async _ => { probes++; await release.Task; }, () => now);
        var a = gate.CheckAsync();
        var b = gate.CheckAsync();
        release.SetResult();
        Assert.True(await a);
        Assert.True(await b);
        Assert.Equal(1, probes);
    }

    [Fact]
    public async Task Weather_reported_elsewhere_counts_as_online()
    {
        int probes = 0;
        var gate = new ConnectivityGate(_ => { probes++; return Task.CompletedTask; }, () => now);
        gate.Report(true);
        Assert.True(await gate.CheckAsync());
        Assert.Equal(0, probes);
    }

    [Fact]
    public async Task No_ai_request_when_forecast_is_unavailable()
    {
        var inner = new CountingClient();
        bool weatherUp = false;
        var gate = new ConnectivityGate(_ => weatherUp ? Task.CompletedTask : throw new HttpRequestException("нет сети"), () => now);
        var client = new GatedAiClient(inner, gate);
        var msg = new[] { new ChatMessage(true, "Bonjour", now) };

        var e = await Assert.ThrowsAsync<AiException>(() => client.CompleteAsync("SYS", msg));
        Assert.Equal(ConnectivityGate.OfflineMessage, e.Message);
        Assert.Equal(0, inner.Calls);                               // запрос к ИИ не ушёл

        weatherUp = true;
        now += TimeSpan.FromMinutes(2);
        Assert.Equal("ответ", await client.CompleteAsync("SYS", msg));
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public void Create_wraps_clients_only_when_setting_is_on()
    {
        var old = AiClients.Gate;
        try
        {
            AiClients.Gate = new ConnectivityGate(_ => Task.CompletedTask);
            var on = new AppSettings { AiProvider = AiProvider.Anthropic, AiAnthropicKey = "k" };
            Assert.IsType<GatedAiClient>(AiClients.Create(on));
            Assert.True(new AppSettings().AiRequireWeather);         // по умолчанию — проверять
            var off = new AppSettings { AiProvider = AiProvider.Anthropic, AiAnthropicKey = "k", AiRequireWeather = false };
            Assert.IsType<AnthropicAiClient>(AiClients.Create(off));
        }
        finally { AiClients.Gate = old; }
    }
}
