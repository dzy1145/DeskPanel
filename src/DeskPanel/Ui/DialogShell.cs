using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DeskPanel.Ui;

/// <summary>
/// 无系统标题栏的深色对话框外壳：顶部标题 + 关闭，可拖动，Esc 关闭。
/// 子类把内容放进 <see cref="Body"/>。
/// </summary>
internal class DialogShell : Window
{
    protected readonly Grid Body = new();

    public DialogShell(string title, double width, double height)
    {
        Title = title;
        Width = width;
        Height = height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = true;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Theme.Ui;

        var card = new Border
        {
            Background = Theme.DialogBg,
            BorderBrush = Theme.HairStrong,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            ClipToBounds = true,
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var bar = new Grid { Background = Brushes.Transparent };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = title,
            FontFamily = Theme.Ui,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
        };
        bar.Children.Add(label);

        var close = new Border
        {
            Width = 28,
            Height = 28,
            Margin = new Thickness(0, 0, 10, 0),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "\u2715",
                FontFamily = Theme.Ui,
                FontSize = 12,
                Foreground = Theme.Dim,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        close.MouseEnter += (_, _) => close.Background = Theme.Hover;
        close.MouseLeave += (_, _) => close.Background = Brushes.Transparent;
        close.MouseLeftButtonUp += (_, e) => { e.Handled = true; Close(); };
        Grid.SetColumn(close, 1);
        bar.Children.Add(close);

        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };

        root.Children.Add(bar);

        Body.Margin = new Thickness(16, 4, 16, 16);
        Grid.SetRow(Body, 1);
        root.Children.Add(Body);

        // 顶部 1px 分隔
        var hair = new Border { Height = 1, Background = Theme.Hair, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetRow(hair, 0);
        root.Children.Add(hair);

        card.Child = root;
        Content = card;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }
}
