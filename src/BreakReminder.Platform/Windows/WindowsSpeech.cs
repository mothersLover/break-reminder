using System.Speech.Synthesis;

namespace BreakReminder.Platform.Windows;

/// <summary>
/// Озвучка через речевой движок Windows (SAPI, System.Speech). Голоса ставятся вместе с языковыми пакетами:
/// Параметры → Время и язык → Речь → «Добавить голоса».
/// </summary>
sealed class WindowsSpeech : IDisposable
{
    SpeechSynthesizer? synth;
    TaskCompletionSource<bool>? current;
    Prompt? currentPrompt;

    SpeechSynthesizer Synth
    {
        get
        {
            if (synth != null) return synth;
            synth = new SpeechSynthesizer();
            synth.SetOutputToDefaultAudioDevice();
            synth.SpeakCompleted += (_, e) =>
            {
                if (e.Prompt == currentPrompt) current?.TrySetResult(true);
            };
            return synth;
        }
    }

    public string? VoiceFor(string lang)
    {
        if (string.IsNullOrEmpty(lang)) return null;   // "" — без озвучки
        try
        {
            return Synth.GetInstalledVoices()
                .Where(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName.Equals(lang, StringComparison.OrdinalIgnoreCase))
                .Select(v => v.VoiceInfo.Name)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    public Task<bool> SpeakAsync(string text, string lang, bool slow, CancellationToken ct)
    {
        var voice = VoiceFor(lang);
        if (voice == null || string.IsNullOrWhiteSpace(text)) return Task.FromResult(false);
        Stop();
        var s = Synth;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            s.SelectVoice(voice);
            s.Rate = slow ? -3 : 0;      // -10…10
            s.Volume = 100;
            current = tcs;
            currentPrompt = s.SpeakAsync(text);
        }
        catch
        {
            return Task.FromResult(false);
        }
        var prompt = currentPrompt;
        ct.Register(() =>
        {
            try { s.SpeakAsyncCancel(prompt); } catch { }
            tcs.TrySetResult(true);
        });
        return tcs.Task;
    }

    public void Stop()
    {
        try { synth?.SpeakAsyncCancelAll(); } catch { }
        current?.TrySetResult(true);
        current = null;
        currentPrompt = null;
    }

    public void Dispose()
    {
        Stop();
        synth?.Dispose();
        synth = null;
    }
}
