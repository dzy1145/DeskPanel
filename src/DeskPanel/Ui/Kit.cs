using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DeskPanel.Ui;

/// <summary>对话框里用的小零件，风格对齐 MicroKit：细描边、圆角、按下回弹。</summary>
internal static class Kit
{
    public static TextBlock Label(string text) => new()
    {
        Text = text,
        FontFamily = Theme.Ui,
        FontSize = 11,
        Foreground = Theme.Dim,
        Margin = new Thickness(0, 0, 0, 6),
    };

    public static TextBox Input(string value = "", bool multiline = false)
    {
        var box = new TextBox
        {
            Text = value ?? "",
            FontFamily = Theme.Ui,
            FontSize = 13,
            Foreground = Theme.Text,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CaretBrush = Theme.Text,
            SelectionBrush = Theme.Frozen(0x66, 0x7A, 0xF0, 0xD8),
            Padding = new Thickness(10, 7, 10, 7),
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        return box;
    }

    /// <summary>输入框外壳：给 TextBox 加圆角和描边（TextBox 自己没有 CornerRadius）。</summary>
    public static Border Field(TextBox box)
    {
        var wrap = new Border
        {
            Background = Theme.Field,
            BorderBrush = Theme.HairStrong,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Child = box,
        };
        box.GotKeyboardFocus += (_, _) => wrap.BorderBrush = Theme.Frozen(0x55, 0x7A, 0xF0, 0xD8);
        box.LostKeyboardFocus += (_, _) => wrap.BorderBrush = Theme.HairStrong;
        return wrap;
    }

    public static Border Button(string text, bool primary, Action onClick)
    {
        var fg = primary ? Theme.Frozen(0xFF, 0x0A, 0x0A, 0x0C) : Theme.Text;
        var bg = primary ? Theme.Text : Brushes.Transparent;

        var label = new TextBlock
        {
            Text = text,
            FontFamily = Theme.Ui,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = fg,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var btn = new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(16, 8, 16, 8),
            Background = bg,
            BorderBrush = primary ? Brushes.Transparent : Theme.HairStrong,
            BorderThickness = new Thickness(primary ? 0 : 1),
            Child = label,
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
        };

        btn.MouseEnter += (_, _) => btn.Opacity = primary ? 0.92 : 1;
        btn.MouseLeave += (_, _) => { btn.Opacity = 1; Scale(btn, 1); };
        btn.PreviewMouseLeftButtonDown += (_, _) => Scale(btn, 0.97);
        btn.PreviewMouseLeftButtonUp += (_, e) => { Scale(btn, 1); e.Handled = true; onClick(); };

        return btn;
    }

    private static void Scale(UIElement el, double to)
    {
        if (el.RenderTransform is ScaleTransform st)
        {
            st.BeginAnimation(ScaleTransform.ScaleXProperty, Theme.Anim(to, 110));
            st.BeginAnimation(ScaleTransform.ScaleYProperty, Theme.Anim(to, 110));
        }
    }

    public static Border Ghost(string text, Action onClick)
    {
        var btn = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 5, 10, 5),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = text, FontFamily = Theme.Ui, FontSize = 11.5, Foreground = Theme.Dim },
        };
        btn.MouseEnter += (_, _) => btn.Background = Theme.Hover;
        btn.MouseLeave += (_, _) => btn.Background = Brushes.Transparent;
        btn.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return btn;
    }
    /// <summary>密钥输入框：默认显示为圆点，右侧「显示 / 隐藏」可临时明文查看。</summary>
    public static (Border host, PasswordBox box) Secret(string value)
    {
        var box = new PasswordBox
        {
            Password = value ?? "",
            FontFamily = Theme.Ui,
            FontSize = 13,
            Foreground = Theme.Text,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CaretBrush = Theme.Text,
            Padding = new Thickness(10, 7, 10, 7),
            PasswordChar = '\u25CF',
        };

        var reveal = new Border
        {
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 6, 0),
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "显示",
                FontFamily = Theme.Ui,
                FontSize = 10.5,
                Foreground = Theme.Faint,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        bool shown = false;
        reveal.MouseEnter += (_, _) => reveal.Background = Theme.Hover;
        reveal.MouseLeave += (_, _) => reveal.Background = Brushes.Transparent;
        reveal.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            shown = !shown;
            box.PasswordChar = shown ? '\0' : '\u25CF';
            if (reveal.Child is TextBlock t) t.Text = shown ? "隐藏" : "显示";
        };

        var inner = new Grid();
        box.Padding = new Thickness(10, 7, 46, 7);
        inner.Children.Add(box);
        inner.Children.Add(reveal);

        var wrap = new Border
        {
            Background = Theme.Field,
            BorderBrush = Theme.HairStrong,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            MinWidth = 210,
            Child = inner,
        };
        box.GotKeyboardFocus += (_, _) => wrap.BorderBrush = Theme.Frozen(0x55, 0x7A, 0xF0, 0xD8);
        box.LostKeyboardFocus += (_, _) => wrap.BorderBrush = Theme.HairStrong;
        return (wrap, box);
    }
}
