using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using BreakReminder.Core;

namespace BreakReminder.App;

/// <summary>
/// Установка обновления: скачать архив своей системы из релиза, сверить SHA-256, распаковать,
/// подменить исполняемый файл и перезапуститься. Windows и Linux; на macOS — только страница релиза.
/// </summary>
static class Updater
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static bool CanSelfUpdate => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь к программе.");
    static string ExeName => OperatingSystem.IsWindows() ? "BreakReminder.exe" : "BreakReminder";

    /// <summary>Скачать и подготовить новую версию рядом с текущей. Возвращает путь к подготовленному файлу.</summary>
    public static async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken ct)
    {
        string assetName = UpdateChecker.AssetNameFor(UpdateChecker.CurrentRid);
        var asset = release.Asset(assetName) ?? throw new InvalidOperationException($"В релизе нет сборки для этой системы ({assetName}).");

        string exeDir = Path.GetDirectoryName(ExePath)!;
        if (!CanWrite(exeDir))
            throw new InvalidOperationException("Нет прав на запись в папку программы. Скачай новую версию со страницы релиза.");

        string work = Path.Combine(Path.GetTempPath(), "break-reminder-update-" + release.Version);
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        string archive = Path.Combine(work, asset.Name);

        // 1. скачать
        using (var resp = await Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Не удалось скачать обновление (ошибка {(int)resp.StatusCode}).");
            long total = resp.Content.Headers.ContentLength ?? asset.Size;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(archive);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress.Report((double)done / total);
            }
        }

        // 2. сверить контрольную сумму (если релиз её публикует — новые релизы публикуют всегда)
        var sumsAsset = release.Asset(UpdateChecker.ChecksumsFile);
        if (sumsAsset != null)
        {
            string sums = await Http.GetStringAsync(sumsAsset.Url, ct);
            string expected = UpdateChecker.ExpectedSha256(sums, asset.Name)
                              ?? throw new InvalidOperationException("В списке контрольных сумм нет этого файла — обновление не установлено.");
            string actual;
            await using (var f = File.OpenRead(archive)) actual = Convert.ToHexString(await SHA256.HashDataAsync(f, ct)).ToLowerInvariant();
            if (actual != expected)
                throw new InvalidOperationException("Скачанный файл повреждён (контрольная сумма не совпала) — обновление не установлено.");
        }

        // 3. распаковать
        string extracted = Path.Combine(work, "x");
        if (asset.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(extracted);
            await using var fs = File.OpenRead(archive);
            await using var gz = new GZipStream(fs, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gz, extracted, true, ct);
        }
        else
        {
            ZipFile.ExtractToDirectory(archive, extracted, true);
        }
        string newExe = Directory.EnumerateFiles(extracted, ExeName, SearchOption.AllDirectories).FirstOrDefault()
                        ?? throw new InvalidOperationException("В архиве обновления нет программы.");

        // 4. положить рядом с текущей (тот же диск — замена будет мгновенной)
        string staged = ExePath + ".new";
        File.Copy(newExe, staged, true);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(staged, File.GetUnixFileMode(ExePath) | UnixFileMode.UserExecute);
        try { Directory.Delete(work, true); } catch { }
        return staged;
    }

    /// <summary>Подменить исполняемый файл и запустить новую версию. После этого текущий процесс должен выйти.</summary>
    public static void ApplyAndRestart(string staged, bool startHidden)
    {
        string exe = ExePath;
        string old = exe + ".old";
        if (OperatingSystem.IsWindows())
        {
            // Запущенный .exe нельзя перезаписать, но можно переименовать.
            File.Move(exe, old, true);
            try { File.Move(staged, exe); }
            catch
            {
                File.Move(old, exe, true); // откат
                throw;
            }
        }
        else
        {
            // На Linux переименование поверх работающего файла безопасно.
            File.Move(staged, exe, true);
        }

        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        psi.ArgumentList.Add("--after-update");
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        if (startHidden) psi.ArgumentList.Add("--tray");
        Process.Start(psi);
    }

    /// <summary>Новая версия ждёт, пока старая завершится, и убирает её файл.</summary>
    public static void FinishAfterUpdate(int oldPid)
    {
        try
        {
            using var p = Process.GetProcessById(oldPid);
            p.WaitForExit(15000);
        }
        catch { /* уже завершился */ }
        try { File.Delete(ExePath + ".old"); } catch { }
    }

    static bool CanWrite(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, ".br-write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }
}
