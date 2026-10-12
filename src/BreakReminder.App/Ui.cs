using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Controls;

namespace BreakReminder.App;

static class Palette
{
    public static readonly Color Bg = Color.Parse("#1E1F26");
    public static readonly Color Card = Color.Parse("#2A2C38");
    public static readonly Color Text = Color.Parse("#ECEFF4");
    public static readonly Color Muted = Color.Parse("#8C92A5");
    public static readonly Color Green = Color.Parse("#2ECC71");
    public static readonly Color Red = Color.Parse("#E74C3C");
    public static readonly Color Amber = Color.Parse("#F39C12");
    public static readonly Color Purple = Color.Parse("#9B59B6");
    public static readonly Color PurpleLight = Color.Parse("#C39BD3");
    public static readonly Color Blue = Color.Parse("#3498DB");
    public static readonly Color Gray = Color.Parse("#484C5E");

    static readonly Dictionary<Color, IBrush> Cache = new();

    /// <summary>Кэшированная кисть цвета.</summary>
    public static IBrush B(Color c)
    {
        if (!Cache.TryGetValue(c, out var b)) Cache[c] = b = new ImmutableSolidColorBrush(c);
        return b;
    }
}

static class Glyphs
{
    public static readonly Geometry Pause = Geometry.Parse("M0,0 H4 V14 H0 Z M8,0 H12 V14 H8 Z");
    public static readonly Geometry Play = Geometry.Parse("M0,0 L12,7 L0,14 Z");
    public static readonly Geometry Chart = Geometry.Parse("M0,14 V8 H3 V14 Z M5,14 V2 H8 V14 Z M10,14 V5 H13 V14 Z");
    // Material Design Icons «cog» (Apache 2.0)
    public static readonly Geometry Gear = Geometry.Parse(
        "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.21,8.95 2.27,9.22 2.46,9.37L4.57,11C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.21,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.67 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z");
}

static class Icons
{
    static readonly Dictionary<Color, WindowIcon> Cache = new();

    /// <summary>Иконка-«часики» нужного цвета (окно и трей).</summary>
    public static WindowIcon Clock(Color c)
    {
        if (Cache.TryGetValue(c, out var icon)) return icon;
        var bmp = new RenderTargetBitmap(new PixelSize(64, 64), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            ctx.DrawEllipse(new SolidColorBrush(c), null, new Point(32, 32), 30, 30);
            var pen = new Pen(Brushes.White, 6.5, lineCap: PenLineCap.Round);
            ctx.DrawLine(pen, new Point(32, 32), new Point(32, 13));
            ctx.DrawLine(pen, new Point(32, 32), new Point(46, 39));
        }
        using var ms = new MemoryStream();
        bmp.Save(ms);
        ms.Position = 0;
        return Cache[c] = new WindowIcon(ms);
    }
}

static class WindowExt
{
    public static IntPtr Handle(this TopLevel w) => w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
}
