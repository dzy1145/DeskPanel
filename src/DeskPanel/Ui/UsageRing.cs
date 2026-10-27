using System.Windows;
using System.Windows.Media;

namespace DeskPanel.Ui;

/// <summary>
/// 一个额度环：
/// - 内环 = 额度已用百分比（颜色按用量分档：≤60% 薄荷、60–85% 琥珀、&gt;85% 红）；
/// - 外环 = 这个窗口的「刷新进度」（窗口走过的时间占比，暗紫色，无数字）；
/// - 中心数字、下方「标签 · 倒计时」。
/// 内外环之间留一个环的空隙。
/// </summary>
internal sealed class UsageRing : FrameworkElement
{
    private int _percent;
    private string _label = "";
    private string _sub = "";
    private bool _hasData;
    private bool _ok = true;
    private DateTime _resetsAt;
    private TimeSpan _window = TimeSpan.Zero;

    public string Label
    {
        get => _label;
        set { _label = value; InvalidateVisual(); }
    }

    /// <summary>窗口长度（5h / 7d / 30d），用来算外环的刷新进度。</summary>
    public TimeSpan Window
    {
        get => _window;
        set { _window = value; InvalidateVisual(); }
    }

    public void Set(int percent, DateTime resetsAt, bool hasData, bool ok = true)
    {
        _percent = Math.Clamp(percent, 0, 100);
        _hasData = hasData;
        _ok = ok;
        _resetsAt = resetsAt;

        if (!hasData)
        {
            _sub = "--";
        }
        else if (resetsAt == default)
        {
            _sub = "";
        }
        else
        {
            var left = resetsAt.ToLocalTime() - DateTime.Now;
            _sub = left <= TimeSpan.Zero ? "已重置"
                : left.TotalDays >= 1 ? $"{(int)left.TotalDays}d{left.Hours}h"
                : left.TotalHours >= 1 ? $"{(int)left.TotalHours}h{left.Minutes}m"
                : $"{Math.Max(1, left.Minutes)}m";
        }

        InvalidateVisual();
    }

    private Brush ColorFor()
    {
        if (!_hasData || !_ok) return Theme.Faint;
        if (_percent > 85) return Theme.Danger;
        if (_percent >= 60) return Theme.Warn;
        return Theme.Accent;
    }

    /// <summary>窗口已走过的时间占比（0–1）。</summary>
    private double RefreshProgress()
    {
        if (_window <= TimeSpan.Zero || _resetsAt == default) return 0;

        var left = _resetsAt.ToLocalTime() - DateTime.Now;
        double remaining = Math.Clamp(left.TotalSeconds, 0, _window.TotalSeconds);
        return Math.Clamp((_window.TotalSeconds - remaining) / _window.TotalSeconds, 0, 1);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double box = Math.Min(w, h - 22);
        if (box <= 12) return;

        const double innerThickness = 4.5;
        const double outerThickness = 2.0;
        const double gap = 4.0;

        double outerR = box / 2 - outerThickness / 2;
        double innerR = outerR - outerThickness / 2 - gap - innerThickness / 2;
        if (innerR <= 3) return;

        var center = new Point(w / 2, box / 2);
        var dpi = VisualTreeHelper.GetDpi(this);

        // ---- 外环：刷新进度（暗紫，无数字）----
        dc.DrawEllipse(null, new Pen(Theme.Hair, outerThickness), center, outerR, outerR);
        double progress = RefreshProgress();
        if (_hasData && progress > 0.004)
        {
            var outerPen = new Pen(Theme.Purple, outerThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, outerPen, Arc(center, outerR, 359.999 * progress));
        }

        // ---- 内环：已用额度 ----
        dc.DrawEllipse(null, new Pen(Theme.Track, innerThickness), center, innerR, innerR);
        if (_hasData && _percent > 0)
        {
            var innerPen = new Pen(ColorFor(), innerThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, innerPen, Arc(center, innerR, 359.999 * _percent / 100.0));
        }

        // ---- 中心数字 ----
        // 用字形几何而不是 FormattedText 的排版框来居中：字体有 ascent/descent，
        // 按 Height/2 对齐会让数字在视觉上偏下（数字没有下伸部分）。
        var text = new FormattedText(
            _hasData ? _percent.ToString() : "--",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(Theme.Num, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            Math.Max(11, innerR * 0.62),
            ColorFor(),
            dpi.PixelsPerDip);

        var glyphs = text.BuildGeometry(new Point(0, 0));
        var ink = glyphs.Bounds;
        if (ink.IsEmpty || ink.Width <= 0 || ink.Height <= 0)
        {
            dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
        }
        else
        {
            dc.PushTransform(new TranslateTransform(
                center.X - (ink.X + ink.Width / 2),
                center.Y - (ink.Y + ink.Height / 2)));
            dc.DrawGeometry(ColorFor(), null, glyphs);
            dc.Pop();
        }

        // ---- 下方：标签 · 倒计时 ----
        var sub = new FormattedText(
            string.IsNullOrEmpty(_sub) ? _label : $"{_label} · {_sub}",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(Theme.Ui, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            10,
            Theme.Faint,
            dpi.PixelsPerDip);

        dc.DrawText(sub, new Point(w / 2 - sub.Width / 2, box + 6));
    }

    private static Geometry Arc(Point center, double r, double sweepDegrees)
    {
        double start = -90 * Math.PI / 180;
        double end = (-90 + sweepDegrees) * Math.PI / 180;

        var p1 = new Point(center.X + r * Math.Cos(start), center.Y + r * Math.Sin(start));
        var p2 = new Point(center.X + r * Math.Cos(end), center.Y + r * Math.Sin(end));

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(p1, false, false);
            ctx.ArcTo(p2, new Size(r, r), 0, sweepDegrees > 180, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        return geo;
    }
}
