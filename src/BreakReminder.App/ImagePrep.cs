using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BreakReminder.Core;

namespace BreakReminder.App;

/// <summary>
/// Подготовка фото для помощника: копия в папку разговора; большие (больше ~3,5 МБ или 2000 пикселей по стороне)
/// уменьшаются — сервисы ИИ не принимают слишком большие картинки, а лишние пиксели только тратят лимиты.
/// </summary>
static class ImagePrep
{
    const long MaxBytes = 3_500_000;
    const int MaxSide = 2000, TargetSide = 1568;

    public static string Prepare(string source, string dir)
    {
        string ext = Path.GetExtension(source).ToLowerInvariant();
        if (!AiImages.Extensions.Contains(ext)) throw new AiException($"«{Path.GetFileName(source)}» — не фото. Подходят JPG, PNG, WEBP, GIF.");
        Directory.CreateDirectory(dir);
        string name = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];

        Bitmap bmp;
        try
        {
            using var f = File.OpenRead(source);
            bmp = new Bitmap(f);
        }
        catch { throw new AiException($"Не получилось открыть «{Path.GetFileName(source)}» как картинку."); }

        using (bmp)
        {
            var size = bmp.PixelSize;
            if (new FileInfo(source).Length <= MaxBytes && Math.Max(size.Width, size.Height) <= MaxSide)
            {
                string copy = Path.Combine(dir, name + ext);
                File.Copy(source, copy);
                return copy;
            }
            // уменьшаем (PNG), пока не влезет в лимит
            int side = TargetSide;
            for (int attempt = 0; attempt < 4; attempt++, side = side * 3 / 4)
            {
                double k = Math.Min(1.0, (double)side / Math.Max(size.Width, size.Height));
                var target = new PixelSize(Math.Max(1, (int)(size.Width * k)), Math.Max(1, (int)(size.Height * k)));
                using var scaled = bmp.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
                string png = Path.Combine(dir, name + ".png");
                scaled.Save(png);
                if (new FileInfo(png).Length <= MaxBytes) return png;
                File.Delete(png);
            }
            throw new AiException($"Фото «{Path.GetFileName(source)}» слишком большое даже после уменьшения.");
        }
    }
}
