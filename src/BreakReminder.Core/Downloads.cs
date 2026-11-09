using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace BreakReminder.Core;

/// <summary>
/// Скачивание файла с повторами на временных сбоях (502/503/504, 408, 429, обрыв связи, таймаут)
/// и докачкой: следующая попытка продолжает с того места, где оборвалось, если сервер это умеет (Range).
/// </summary>
public static class Downloads
{
    /// <summary>Паузы между попытками: всего попыток — на одну больше.</summary>
    public static readonly TimeSpan[] RetryDelays = { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) };

    /// <summary>Временная ошибка сервера — стоит повторить.</summary>
    sealed class TransientHttpException(int status) : Exception($"HTTP {status}")
    {
        public int Status { get; } = status;
    }

    static bool IsTransient(HttpStatusCode code) =>
        code is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)code >= 500;

    /// <param name="expectedSize">Размер из описания релиза (0 — неизвестен) — для прогресса.</param>
    /// <param name="retrying">Номер следующей попытки и сколько их всего — чтобы показать «попытка 2 из 4».</param>
    /// <param name="delays">Паузы между попытками (для тестов — короткие).</param>
    public static async Task ToFileAsync(HttpClient http, string url, string path, long expectedSize,
        IProgress<double>? progress, Action<int, int>? retrying, CancellationToken ct, IReadOnlyList<TimeSpan>? delays = null)
    {
        delays ??= RetryDelays;
        int attempts = delays.Count + 1;
        Exception? last = null;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if (attempt > 0)
            {
                retrying?.Invoke(attempt + 1, attempts);
                await Task.Delay(delays[attempt - 1], ct).ConfigureAwait(false);
            }
            try
            {
                await Once(http, url, path, expectedSize, progress, ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) when (e is TransientHttpException or HttpRequestException or IOException or TaskCanceledException)
            {
                last = e;   // временный сбой — ещё попытка (уже скачанное сохраняется для докачки)
            }
        }
        throw new InvalidOperationException(last switch
        {
            TransientHttpException t => $"GitHub сейчас не отвечает (ошибка {t.Status}) — {attempts} попытки не помогли. Попробуй ещё раз через пару минут.",
            TaskCanceledException => "GitHub отвечает слишком медленно — скачать не получилось. Попробуй ещё раз позже.",
            _ => "Нет связи с GitHub — проверь интернет и попробуй ещё раз."
        });
    }

    static async Task Once(HttpClient http, string url, string path, long expectedSize, IProgress<double>? progress, CancellationToken ct)
    {
        long have = File.Exists(path) ? new FileInfo(path).Length : 0;
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (have > 0) req.Headers.Range = new RangeHeaderValue(have, null);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && have > 0)
        {
            if (expectedSize > 0 && have == expectedSize) { progress?.Report(1); return; }   // уже всё скачано
            File.Delete(path);
            throw new TransientHttpException(416);
        }
        if (!resp.IsSuccessStatusCode)
        {
            if (IsTransient(resp.StatusCode)) throw new TransientHttpException((int)resp.StatusCode);
            throw new InvalidOperationException($"Не удалось скачать обновление (ошибка {(int)resp.StatusCode}).");
        }

        bool append = have > 0 && resp.StatusCode == HttpStatusCode.PartialContent;   // сервер продолжил с места обрыва
        if (!append) have = 0;
        long? length = resp.Content.Headers.ContentLength;
        long total = length is { } l ? have + l : expectedSize;

        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = new FileStream(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write);
        var buf = new byte[81920];
        long done = have;
        int n;
        while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
            done += n;
            if (total > 0) progress?.Report(Math.Min(1, (double)done / total));
        }
        if (length is { } expected && done - have < expected) throw new IOException("Соединение оборвалось.");
    }
}
