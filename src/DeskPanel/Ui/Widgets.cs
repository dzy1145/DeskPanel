using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace DeskPanel.Ui;

/// <summary>设置界面用的小控件，风格与面板一致：细描边、大圆角、微动效。</summary>
internal static class Widgets
{
    // ---------------------------------------------------------------- 卡片

    public static Border Card(params UIElement[] children)
    {
        var panel = new StackPanel();
        foreach (var child in children) panel.Children.Add(child);

        return new Border
        {
            Background = Theme.Frozen(0x08, 0xFF, 0xFF, 0xFF),
            BorderBrush = Theme.Hair,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 12),
            Child = panel,
        };
    }

    /// <summary>一行设置项：左边标题 + 说明，右边控件。</summary>
    public static Grid Row(string title, string desc, UIElement control, bool divider = true)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 0), MinHeight = 44 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 12, 8) };
        texts.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = Theme.Ui,
            FontSize = 12.5,
            Foreground = Theme.Text,
        });
        if (!string.IsNullOrWhiteSpace(desc))
        {
            texts.Children.Add(new TextBlock
            {
                Text = desc,
                FontFamily = Theme.Ui,
                FontSize = 10.5,
                Foreground = Theme.Faint,
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        grid.Children.Add(texts);

        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);

        if (!divider) return grid;

        var host = new Grid();
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.Children.Add(grid);

        var hair = new Border { Height = 1, Background = Theme.Hair, Margin = new Thickness(0, 2, 0, 2) };
        Grid.SetRow(hair, 1);
        host.Children.Add(hair);
        return host;
    }

    // ---------------------------------------------------------------- 开关

    public sealed class Switch : Border
    {
        private readonly Border _knob;
        private bool _on;

        public event Action<bool> Changed;

        public bool IsOn
        {
            get => _on;
            set { _on = value; Apply(false); }
        }

        public Switch(bool on)
        {
            Width = 42;
            Height = 24;
            CornerRadius = new CornerRadius(12);
            BorderThickness = new Thickness(1);
            Cursor = Cursors.Hand;
            Background = Brushes.Transparent;

            _knob = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0),
                RenderTransform = new TranslateTransform(0, 0),
            };
            Child = _knob;

            _on = on;
            Apply(false);

            MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _on = !_on;
                Apply(true);
                Changed?.Invoke(_on);
            };
        }

        private void Apply(bool animate)
        {
            var track = _on ? Theme.Frozen(0x33, 0x7A, 0xF0, 0xD8) : Theme.Track;
            var knob = _on ? Theme.Accent : Theme.Frozen(0xFF, 0x6E, 0x6E, 0x76);
            BorderBrush = _on ? Theme.Frozen(0x55, 0x7A, 0xF0, 0xD8) : Theme.HairStrong;
            Background = track;
            _knob.Background = knob;

            double to = _on ? 18 : 0;
            if (_knob.RenderTransform is TranslateTransform tt)
            {
                if (animate) tt.BeginAnimation(TranslateTransform.XProperty, Theme.Anim(to, 160));
                else tt.X = to;
            }
        }
    }

    // ---------------------------------------------------------------- 分段选择

    public sealed class Segmented : Border
    {
        private readonly StackPanel _items = new() { Orientation = Orientation.Horizontal };
        private readonly List<(string key, Border host, TextBlock label)> _buttons = new();
        private string _value;

        public event Action<string> Changed;

        public string Value
        {
            get => _value;
            set { _value = value; Sync(); }
        }

        public Segmented(string value, params (string key, string text)[] options)
        {
            _value = value;
            Background = Theme.Frozen(0x0A, 0xFF, 0xFF, 0xFF);
            BorderBrush = Theme.HairStrong;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(9);
            Padding = new Thickness(3);
            Child = _items;

            foreach (var (key, text) in options)
            {
                var label = new TextBlock
                {
                    Text = text,
                    FontFamily = Theme.Ui,
                    FontSize = 11.5,
                    Foreground = Theme.Dim,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var host = new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(12, 5, 12, 5),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    Child = label,
                };
                string captured = key;
                host.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    Value = captured;
                    Changed?.Invoke(captured);
                };
                _items.Children.Add(host);
                _buttons.Add((key, host, label));
            }

            Sync();
        }

        private void Sync()
        {
            foreach (var (key, host, label) in _buttons)
            {
                bool on = string.Equals(key, _value, StringComparison.OrdinalIgnoreCase);
                host.Background = on ? Theme.Frozen(0x1F, 0xFF, 0xFF, 0xFF) : Brushes.Transparent;
                label.Foreground = on ? Theme.Text : Theme.Dim;
                label.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
    }

    // ---------------------------------------------------------------- 步进数字

    public sealed class Stepper : StackPanel
    {
        private readonly TextBlock _value;
        private int _current;
        private readonly int _min, _max, _step;

        public event Action<int> Changed;

        public int Value
        {
            get => _current;
            set { _current = Clamp(value); _value.Text = _current + _suffix; }
        }

        public Stepper(int value, int min, int max, int step, string suffix = "")
        {
            Orientation = Orientation.Horizontal;
            _min = min; _max = max; _step = step;
            _current = Clamp(value);

            var minus = Btn("\u2212", () => Nudge(-_step));
            var plus = Btn("+", () => Nudge(_step));

            _value = new TextBlock
            {
                Text = _current + suffix,
                FontFamily = Theme.Num,
                FontSize = 12.5,
                Foreground = Theme.Text,
                MinWidth = 54,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _suffix = suffix;

            Children.Add(minus);
            Children.Add(_value);
            Children.Add(plus);
        }

        private readonly string _suffix;

        private int Clamp(int v) => Math.Max(_min, Math.Min(_max, v));

        private void Nudge(int delta)
        {
            int next = Clamp(_current + delta);
            if (next == _current) return;
            _current = next;
            _value.Text = _current + _suffix;
            var anim = new DoubleAnimation(1.12, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = Theme.Ease,
            };
            _value.RenderTransformOrigin = new Point(0.5, 0.5);
            _value.RenderTransform = new ScaleTransform(1, 1);
            if (_value.RenderTransform is ScaleTransform st)
            {
                st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
            Changed?.Invoke(_current);
        }

        private static Border Btn(string glyph, Action onClick)
        {
            var host = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(7),
                Background = Theme.Frozen(0x10, 0xFF, 0xFF, 0xFF),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = glyph,
                    FontFamily = Theme.Ui,
                    FontSize = 13,
                    Foreground = Theme.Dim,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            host.MouseEnter += (_, _) => host.Background = Theme.Hover;
            host.MouseLeave += (_, _) => host.Background = Theme.Frozen(0x10, 0xFF, 0xFF, 0xFF);
            host.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
            return host;
        }
    }

    // ---------------------------------------------------------------- 可点击的小标签（多选）

    public sealed class Chip : Border
    {
        private bool _on;
        private readonly TextBlock _label;

        public event Action<bool> Changed;

        public bool IsOn => _on;
        public string Key { get; }

        public Chip(string key, bool on)
        {
            Key = key;
            _on = on;
            _label = new TextBlock
            {
                Text = key,
                FontFamily = Theme.Ui,
                FontSize = 11.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            CornerRadius = new CornerRadius(8);
            Padding = new Thickness(11, 5, 11, 5);
            Margin = new Thickness(0, 0, 6, 0);
            Cursor = Cursors.Hand;
            Child = _label;
            Sync();

            MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _on = !_on;
                Sync();
                Changed?.Invoke(_on);
            };
        }

        private void Sync()
        {
            Background = _on ? Theme.Frozen(0x33, 0x7A, 0xF0, 0xD8) : Theme.Frozen(0x0A, 0xFF, 0xFF, 0xFF);
            BorderBrush = _on ? Theme.Frozen(0x66, 0x7A, 0xF0, 0xD8) : Theme.HairStrong;
            BorderThickness = new Thickness(1);
            _label.Foreground = _on ? Theme.Text : Theme.Dim;
            _label.FontWeight = _on ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    // ---------------------------------------------------------------- 页面切换动效

    public static void SlideIn(UIElement element)
    {
        element.Opacity = 0;
        var tt = new TranslateTransform(0, 8);
        element.RenderTransform = tt;

        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(170)) { EasingFunction = Theme.Ease });
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = Theme.Ease });
    }
}
