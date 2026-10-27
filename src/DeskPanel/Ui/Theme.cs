using System.Windows;
using System.Windows.Media;

namespace DeskPanel.Ui;

/// <summary>
/// MicroKit 风：近黑底、1px 细描边、大圆角、单色高对比，强调色只给状态用。
/// </summary>
internal static class Theme
{
    public static readonly FontFamily Ui = new("Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI");
    public static readonly FontFamily Num = new("Segoe UI Variable Display, Bahnschrift, Segoe UI");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    // 文字
    public static readonly Brush Text = Frozen(0xFF, 0xF5, 0xF5, 0xF7);
    public static readonly Brush Dim = Frozen(0xFF, 0x9A, 0x9A, 0xA2);
    public static readonly Brush Faint = Frozen(0xFF, 0x5E, 0x5E, 0x66);

    // 状态色（只用于状态，不铺面）
    public static readonly Brush Accent = Frozen(0xFF, 0x7A, 0xF0, 0xD8);
    public static readonly Brush Purple = Frozen(0xFF, 0x9B, 0xA2, 0xFF);
    public static readonly Brush Warn = Frozen(0xFF, 0xFF, 0xC2, 0x4D);
    public static readonly Brush Danger = Frozen(0xFF, 0xFF, 0x6B, 0x6B);

    // 面 / 线
    public static readonly Brush Track = Frozen(0x1F, 0xFF, 0xFF, 0xFF);
    public static readonly Brush Hair = Frozen(0x14, 0xFF, 0xFF, 0xFF);
    public static readonly Brush HairStrong = Frozen(0x24, 0xFF, 0xFF, 0xFF);
    public static readonly Brush Hover = Frozen(0x0F, 0xFF, 0xFF, 0xFF);
    public static readonly Brush Press = Frozen(0x1A, 0xFF, 0xFF, 0xFF);
    public static readonly Brush Surface = Frozen(0xE8, 0x0B, 0x0B, 0x0D);
    public static readonly Brush SurfaceSolid = Frozen(0xFF, 0x0B, 0x0B, 0x0D);
    public static readonly Brush DialogBg = Frozen(0xFF, 0x11, 0x11, 0x14);
    public static readonly Brush Field = Frozen(0x0A, 0xFF, 0xFF, 0xFF);
    public static readonly Brush DotOff = Frozen(0xFF, 0x3A, 0x3A, 0x40);

    public static SolidColorBrush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    public static Color Argbs(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    /// <summary>统一缓动：快出慢收，像 microkit 那种干脆感。</summary>
    public static System.Windows.Media.Animation.IEasingFunction Ease { get; } = Freeze();

    private static System.Windows.Media.Animation.IEasingFunction Freeze()
    {
        var f = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        f.Freeze();
        return f;
    }

    public static System.Windows.Media.Animation.DoubleAnimation Anim(double to, int ms = 150)
        => new(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Ease };
}
