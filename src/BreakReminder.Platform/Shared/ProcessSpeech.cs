using System.Diagnostics;

namespace BreakReminder.Platform.Shared;

/// <summary>
/// Озвучка внешней командой (Linux: spd-say / espeak-ng, macOS: say). Фраза произносится отдельным процессом;
/// новая фраза или отмена его останавливают.
/// </summary>
sealed class ProcessSpeech
{
    readonly Func<string, string?> voiceFor;
    readonly Func<string, string, bool, string, ProcessStartInfo> command;   // (текст, язык, медленно, голос)
    Process? current;

    public ProcessSpeech(Func<string, string?> voiceFor, Func<string, string, bool, string, ProcessStartInfo> command)
    {
        this.voiceFor = voiceFor;
        this.command = command;
    }

    public string? VoiceFor(string lang) => string.IsNullOrEmpty(lang) ? null : voiceFor(lang);   // "" — без озвучки

    public async Task<bool> SpeakAsync(string text, string lang, bool slow, CancellationToken ct)
    {
        var voice = VoiceFor(lang);
        if (voice == null || string.IsNullOrWhiteSpace(text)) return false;
        Stop();
        var psi = command(text, lang, slow, voice);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = psi.RedirectStandardError = true;
        Process p;
        try { p = Process.Start(psi)!; }
        catch { return false; }
        current = p;
        try { await p.WaitForExitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { Kill(p); }
        finally
        {
            if (current == p) current = null;
            p.Dispose();
        }
        return true;
    }

    public void Stop()
    {
        if (current is { } p) Kill(p);
        current = null;
    }

    static void Kill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
    }

    /// <summary>Команда с аргументами без возни с кавычками.</summary>
    public static ProcessStartInfo Args(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file);
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }
}
