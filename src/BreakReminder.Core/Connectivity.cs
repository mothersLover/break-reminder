namespace BreakReminder.Core;

/// <summary>
/// Проверка связи по прогнозу погоды: если прогноз (Open-Meteo) недоступен — считаем, что связи нет,
/// и ИИ-помощнику запросы не отправляем. Результат кешируется: «есть связь» — на 10 минут, «нет» — на минуту.
/// </summary>
public sealed class ConnectivityGate
{
    public static readonly TimeSpan OnlineTtl = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan OfflineTtl = TimeSpan.FromMinutes(1);
    public const string OfflineMessage = "Нет связи: прогноз погоды недоступен — помощник отключён, запросы не отправляются.";

    readonly Func<CancellationToken, Task> probe;
    readonly Func<DateTime> clock;
    Task<bool>? running;

    /// <param name="probe">Запрос прогноза; исключение — связи нет.</param>
    public ConnectivityGate(Func<CancellationToken, Task> probe, Func<DateTime>? clock = null)
    {
        this.probe = probe;
        this.clock = clock ?? (() => DateTime.Now);
    }

    /// <summary>null — ещё не проверяли.</summary>
    public bool? Online { get; private set; }
    public DateTime CheckedAt { get; private set; } = DateTime.MinValue;
    /// <summary>Почему нет связи (текст ошибки прогноза).</summary>
    public string? Error { get; private set; }

    /// <summary>Состояние изменилось (могут вызвать не из UI-потока).</summary>
    public event Action? Changed;

    bool Fresh => Online is { } ok && clock() - CheckedAt < (ok ? OnlineTtl : OfflineTtl) && clock() >= CheckedAt;

    /// <summary>Есть ли связь: из кеша или новой проверкой (force — проверить заново). Одновременные проверки объединяются.</summary>
    public Task<bool> CheckAsync(bool force = false, CancellationToken ct = default)
    {
        if (!force && Fresh) return Task.FromResult(Online == true);
        if (running is { IsCompleted: false } inFlight) return inFlight;
        return running = Run(ct);
    }

    async Task<bool> Run(CancellationToken ct)
    {
        try
        {
            await probe(ct).ConfigureAwait(false);
            Report(true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            Report(false, e.Message);
        }
        return Online == true;
    }

    /// <summary>Результат, известный из другого места (например, погода для приветствия загрузилась).</summary>
    public void Report(bool ok, string? error = null)
    {
        bool changed = Online != ok;
        Online = ok;
        Error = ok ? null : error;
        CheckedAt = clock();
        if (changed) Changed?.Invoke();
    }
}

/// <summary>Клиент ИИ за «шлагбаумом»: без связи (нет прогноза) запрос не отправляется.</summary>
public sealed class GatedAiClient(IAiClient inner, ConnectivityGate gate) : IAiClient
{
    public async Task<string> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
    {
        if (!await gate.CheckAsync(ct: ct).ConfigureAwait(false)) throw new AiException(ConnectivityGate.OfflineMessage);
        return await inner.CompleteAsync(system, messages, ct).ConfigureAwait(false);
    }
}
