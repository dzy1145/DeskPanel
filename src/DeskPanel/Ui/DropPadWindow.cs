using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeskPanel.Interop;

namespace DeskPanel.Ui;

/// <summary>
/// 独立拖放小窗。注意：这里**不能**用 AllowsTransparency（分层窗口收不到 OLE 拖放），
/// 所以圆角交给 DWM，背景用纯色。
/// </summary>
internal sealed class DropPadWindow : Window
{
    private readonly Action<string[]> _onFiles;
    private readonly TextBlock _hint;
    private int _added;

    public DropPadWindow(Action<string[]> onFiles)
    {
        _onFiles = onFiles;

        Title = "拖放";
        Width = 340;
        Height = 200;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        Background = Theme.DialogBg;
        ShowInTaskbar = true;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Theme.Ui;
        AllowDrop = true;

        var root = new Grid { Margin = new Thickness(20, 16, 20, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "拖到这里",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        head.Children.Add(title);

        var close = Kit.Ghost("\u2715", Close);
        Grid.SetColumn(close, 1);
        head.Children.Add(close);

        root.Children.Add(head);

        _hint = new TextBlock
        {
            Text = "把文件 / 文件夹拖进这个窗口\n会生成快捷方式，放到常用区末尾",
            FontSize = 11.5,
            LineHeight = 18,
            Foreground = Theme.Dim,
            Margin = new Thickness(0, 14, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetRow(_hint, 1);
        root.Children.Add(_hint);

        Content = root;

        // 圆角（DWM），不做透明
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int round = 2; // DWMWCP_ROUND
            try { Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int)); } catch { }
        };

        DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            e.Effects = DragDropEffects.Copy;
            _hint.Text = "松手即可";
            e.Handled = true;
        };

        DragLeave += (_, _) =>
            _hint.Text = "把文件 / 文件夹拖进这个窗口\n会生成快捷方式，放到常用区末尾";

        Drop += (_, e) =>
        {
            e.Handled = true;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

            _added += paths.Length;
            _onFiles?.Invoke(paths);
            _hint.Text = $"已收到 {_added} 项，可以继续拖";
        };
    }
}
