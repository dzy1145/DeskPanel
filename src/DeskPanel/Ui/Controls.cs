using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DeskPanel.Ui;

internal sealed class Bar : FrameworkElement
{
    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(Bar),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    public event Action<double> Animated;

    private double _target;
    private double _value;
    private double _velocity;
    private bool _running;
    private bool _initialized;
    private double _lastTime;

    private const double Omega = 17.0;
    private const double Zeta = 0.58;

    public void SetTarget(double pct)
    {
        _target = Math.Max(0, Math.Min(100, pct));

        if (!_initialized)
        {
            _initialized = true;
            _value = _target;
            _velocity = 0;
            InvalidateVisual();
            Animated?.Invoke(_value);
            return;
        }

        if (!_running)
        {
            _running = true;
            _lastTime = 0;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    private void OnFrame(object sender, EventArgs e)
    {
        double now = e is RenderingEventArgs rea ? rea.RenderingTime.TotalSeconds : _lastTime + 1.0 / 60;
        double dt = _lastTime <= 0 ? 1.0 / 60 : now - _lastTime;
        _lastTime = now;
        dt = Math.Clamp(dt, 0.001, 0.05);

        double accel = Omega * Omega * (_target - _value) - 2 * Zeta * Omega * _velocity;
        _velocity += accel * dt;
        _value += _velocity * dt;

        if (Math.Abs(_target - _value) < 0.08 && Math.Abs(_velocity) < 0.4)
        {
            _value = _target;
            _velocity = 0;
            Stop();
        }

        InvalidateVisual();
        Animated?.Invoke(_value);
    }

    private void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double r = h / 2;
        dc.DrawRoundedRectangle(Theme.Track, null, new Rect(0, 0, w, h), r, r);

        double pct = Math.Max(0, Math.Min(100, _value)) / 100.0;
        double fw = w * pct;
        if (fw < 0.5) return;
        fw = Math.Max(fw, h);
        dc.DrawRoundedRectangle(Accent, null, new Rect(0, 0, fw, h), r, r);
    }
}

internal sealed class MetricRow : Grid
{
    private readonly TextBlock _value;
    private readonly TextBlock _label;
    private readonly TextBlock _meta;
    private readonly Bar _bar;

    public MetricRow(string name, Brush accent, Brush valueBrush = null)
    {
        Margin = new Thickness(0, 0, 0, 15);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _label = new TextBlock
        {
            Text = name,
            FontFamily = Theme.Ui,
            FontSize = 11,
            Foreground = Theme.Dim,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 3),
        };

        _value = new TextBlock
        {
            Text = "--",
            FontFamily = Theme.Num,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = valueBrush ?? Theme.Text,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextAlignment = TextAlignment.Right,
        };

        // 温度/功耗这类附加读数贴在标签右边，不单独占一行
        _meta = new TextBlock
        {
            Text = "",
            FontFamily = Theme.Num,
            FontSize = 10.5,
            Foreground = Theme.Faint,
            Margin = new Thickness(7, 0, 0, 3),
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        var labelBox = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        labelBox.Children.Add(_label);
        labelBox.Children.Add(_meta);

        head.Children.Add(labelBox);

        Grid.SetColumn(_value, 1);
        head.Children.Add(_value);
        Children.Add(head);

        _bar = new Bar
        {
            Height = 2,
            Accent = accent ?? Theme.Text,
            Margin = new Thickness(0, 7, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };

        _bar.Animated += v =>
        {
            string text = $"{v:0}%";
            if (_value.Text != text) _value.Text = text;
        };

        SetRow(_bar, 1);
        Children.Add(_bar);
    }

    public void SetLabel(string text)
    {
        if (_label.Text != text) _label.Text = text;
    }

    /// <summary>标签右边的小字（温度、功耗等）。传 null 或空串即隐藏。</summary>
    public void SetMeta(string text, Brush color = null)
    {
        if (_meta.Text != (text ?? "")) _meta.Text = text ?? "";
        _meta.Foreground = color ?? Theme.Faint;
    }

    public void Set(double pct, Brush color = null)
    {
        _bar.SetTarget(pct);
        if (color != null) _value.Foreground = color;
    }
}

internal sealed class ServiceRow : Grid
{
    private readonly Ellipse _dot;
    private readonly Border _stop;
    private bool _confirming;
    private readonly DispatcherTimer _confirmTimer;

    public event Action StartClicked;
    public event Action StopClicked;
    public event Action ExtraClicked;

    public ServiceRow(string name, bool hasExtra)
    {
        Height = 32;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });

        _dot = new Ellipse { Width = 5, Height = 5, Fill = Theme.DotOff, VerticalAlignment = VerticalAlignment.Center };
        Children.Add(_dot);

        var label = new TextBlock
        {
            Text = name,
            FontFamily = Theme.Ui,
            FontSize = 12.5,
            Foreground = Theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(label, 1);
        Children.Add(label);

        var start = MakeGlyph("\u25B6", Theme.Dim, () => StartClicked?.Invoke());
        Grid.SetColumn(start, 3);
        Children.Add(start);

        _stop = MakeGlyph("\u25A0", Theme.Faint, OnStop);
        Grid.SetColumn(_stop, 4);
        Children.Add(_stop);

        if (hasExtra)
        {
            var extra = MakeGlyph("\u2197", Theme.Faint, () => ExtraClicked?.Invoke());
            Grid.SetColumn(extra, 2);
            Children.Add(extra);
        }

        _confirmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        _confirmTimer.Tick += (_, _) => ResetConfirm();
    }

    public void SetOn(bool on)
    {
        _dot.Fill = on ? Theme.Accent : Theme.DotOff;
        _dot.Opacity = on ? 1 : 0.9;
    }

    private void OnStop()
    {
        if (!_confirming)
        {
            _confirming = true;
            if (_stop.Child is TextBlock t)
            {
                t.Text = "\u2715";
                t.Foreground = Theme.Danger;
            }
            _confirmTimer.Start();
            return;
        }
        ResetConfirm();
        StopClicked?.Invoke();
    }

    private void ResetConfirm()
    {
        _confirming = false;
        _confirmTimer.Stop();
        if (_stop.Child is TextBlock t)
        {
            t.Text = "\u25A0";
            t.Foreground = Theme.Faint;
        }
    }

    private static Border MakeGlyph(string glyph, Brush color, Action onClick)
    {
        var text = new TextBlock
        {
            Text = glyph,
            FontFamily = Theme.Ui,
            FontSize = 11,
            Foreground = color,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var host = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Child = text,
            Cursor = Cursors.Hand,
        };
        host.MouseEnter += (_, _) => host.Background = Theme.Hover;
        host.MouseLeave += (_, _) => host.Background = Brushes.Transparent;
        host.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return host;
    }
}

internal sealed class StartupRow : Grid
{
    private readonly Border _track;
    private readonly Border _thumb;
    private readonly TextBlock _label;
    public bool Enabled { get; private set; }
    public event Action<bool> Toggled;

    public StartupRow(string name, string source, bool enabled)
    {
        Height = 28;
        Enabled = enabled;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        _label = new TextBlock
        {
            Text = name,
            FontFamily = Theme.Ui,
            FontSize = 12,
            Foreground = Theme.Text,
            Opacity = enabled ? 1 : 0.45,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Children.Add(_label);

        var tag = new TextBlock
        {
            Text = source,
            FontFamily = Theme.Mono,
            FontSize = 9,
            Foreground = Theme.Faint,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(tag, 1);
        Children.Add(tag);

        _track = new Border
        {
            Width = 30,
            Height = 17,
            CornerRadius = new CornerRadius(9),
            Background = enabled ? Theme.Accent : Theme.Frozen(0x1A, 0xFF, 0xFF, 0xFF),
            BorderBrush = enabled ? Brushes.Transparent : Theme.HairStrong,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = Cursors.Hand,
        };

        _thumb = new Border
        {
            Width = 13,
            Height = 13,
            CornerRadius = new CornerRadius(7),
            Background = enabled ? Theme.Frozen(0xFF, 0x0A, 0x0A, 0x0C) : Theme.Dim,
            HorizontalAlignment = enabled ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0),
        };
        _track.Child = _thumb;
        _track.MouseLeftButtonUp += (_, e) => { e.Handled = true; Flip(); };

        // 整行可点：开关只有 30×17，按名字或行空白处也应该能切换
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        MouseLeftButtonUp += (_, e) => { e.Handled = true; Flip(); };

        Grid.SetColumn(_track, 2);
        Children.Add(_track);
    }

    private void Flip()
    {
        Enabled = !Enabled;
        _track.Background = Enabled ? Theme.Accent : Theme.Frozen(0x1A, 0xFF, 0xFF, 0xFF);
        _track.BorderBrush = Enabled ? Brushes.Transparent : Theme.HairStrong;
        _thumb.Background = Enabled ? Theme.Frozen(0xFF, 0x0A, 0x0A, 0x0C) : Theme.Dim;
        _thumb.HorizontalAlignment = Enabled ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _label.Opacity = Enabled ? 1 : 0.45;
        Toggled?.Invoke(Enabled);
    }
}

internal sealed class Pill : Border
{
    public Pill(string text, Brush accent, Action onClick)
    {
        CornerRadius = new CornerRadius(999);
        BorderBrush = Theme.HairStrong;
        BorderThickness = new Thickness(1);
        Background = Brushes.Transparent;
        Padding = new Thickness(11, 4, 11, 4);
        Cursor = Cursors.Hand;
        Child = new TextBlock
        {
            Text = text,
            FontFamily = Theme.Ui,
            FontSize = 11,
            Foreground = accent,
        };
        MouseEnter += (_, _) => Background = Theme.Hover;
        MouseLeave += (_, _) => Background = Brushes.Transparent;
        MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
    }
}

internal static class Parts
{
    public static TextBlock Section(string text) => new()
    {
        Text = text,
        FontFamily = Theme.Ui,
        FontSize = 10,
        Foreground = Theme.Faint,
        Margin = new Thickness(0, 0, 0, 10),
    };

    public static Border Divider() => new()
    {
        Height = 1,
        Background = Theme.Hair,
        Margin = new Thickness(0, 16, 0, 16),
    };
}
/// <summary>一行小字读数：左边标签、右边数值，没有进度条（温度这类不适合画比例）。</summary>
internal sealed class SmallRow : Grid
{
    private readonly TextBlock _value;

    public SmallRow(string label)
    {
        Height = 20;
        Margin = new Thickness(0, 2, 0, 0);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = Theme.Ui,
            FontSize = 11,
            Foreground = Theme.Dim,
            VerticalAlignment = VerticalAlignment.Center,
        });

        _value = new TextBlock
        {
            Text = "--",
            FontFamily = Theme.Num,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_value, 1);
        Children.Add(_value);
    }

    public void Set(string text, Brush color = null)
    {
        if (_value.Text != text) _value.Text = text;
        _value.Foreground = color ?? Theme.Text;
    }
}
