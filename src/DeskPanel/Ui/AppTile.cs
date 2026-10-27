using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DeskPanel.Ui;

/// <summary>常用区的一格。程序 / 快捷方式 / 文件夹 / 文件 / 网址都走 ShellExecute，
/// 图标：网址取 favicon（取不到用字母块），其余取 Shell 图标。</summary>
internal sealed class AppTile : Border
{
    public AppItem Item { get; }

    public event Action<AppItem> LaunchRequested;
    public event Action<AppItem> DeleteRequested;
    public event Action<AppItem> EditRequested;
    public event Action<AppItem> RevealRequested;

    private readonly Border _deleteBadge;
    private readonly TextBlock _label;
    private readonly Border _letter;
    private readonly Image _image;
    private bool _editing;

    private static bool IsUrl(string target)
        => !string.IsNullOrWhiteSpace(target) &&
           (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    public AppTile(AppItem item)
    {
        Item = item;
        AllowDrop = true;              // 让 WPF 的拖放命中这一格（外部拖入时要能接住）
        Height = 56;
        Margin = new Thickness(0, 0, 4, 4);
        CornerRadius = new CornerRadius(12);
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = new ScaleTransform(1, 1);
        ToolTip = string.IsNullOrWhiteSpace(item.Target) ? "未设置目标" : item.Target;

        bool url = IsUrl(item.Target);

        var root = new Grid();
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var iconHost = new Grid { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center };

        var (bg, letter) = Favicons.Letter(item.Name, item.Target ?? item.Name);
        _letter = new Border
        {
            CornerRadius = new CornerRadius(7),
            Background = bg,
            Child = new TextBlock
            {
                Text = letter,
                FontFamily = Theme.Ui,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Frozen(0xFF, 0x0B, 0x0C, 0x10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        iconHost.Children.Add(_letter);

        _image = new Image { Width = 24, Height = 24 };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        iconHost.Children.Add(_image);

        string iconPath = string.IsNullOrWhiteSpace(item.Icon) ? item.Target : item.Icon;
        ImageSource src = url ? Favicons.LoadCached(item.Target) : IconHelper.Load(iconPath);
        ApplyIcon(src);

        if (url && src == null)
        {
            string target = item.Target;
            Favicons.FetchAsync(target).ContinueWith(t =>
            {
                if (t.Status != TaskStatus.RanToCompletion || t.Result == null) return;
                Dispatcher.BeginInvoke(new Action(() => ApplyIcon(t.Result)));
            });
        }

        stack.Children.Add(iconHost);

        _label = new TextBlock
        {
            Text = item.Name,
            FontFamily = Theme.Ui,
            FontSize = 10.5,
            Foreground = string.IsNullOrWhiteSpace(item.Target) ? Theme.Faint : Theme.Dim,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 80,
        };
        stack.Children.Add(_label);

        root.Children.Add(stack);

        _deleteBadge = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            Background = Theme.Frozen(0xD9, 0x2A, 0x2D, 0x33),
            BorderBrush = Theme.Frozen(0x40, 0xFF, 0xFF, 0xFF),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(2),
            Visibility = Visibility.Collapsed,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "\u00D7",
                FontFamily = Theme.Ui,
                FontSize = 11,
                Foreground = Theme.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        _deleteBadge.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            DeleteRequested?.Invoke(Item);
        };
        root.Children.Add(_deleteBadge);

        Child = root;

        MouseEnter += (_, _) => { if (!_editing) { Background = Theme.Hover; Scale(1.03); } };
        MouseLeave += (_, _) => { if (!_editing) { Background = Brushes.Transparent; Scale(1); } };
        PreviewMouseLeftButtonDown += (_, _) => Scale(0.96);
        PreviewMouseLeftButtonUp += (_, _) => Scale(1);
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!_editing) LaunchRequested?.Invoke(Item);
        };
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowMenu();
        };
    }

    private void ApplyIcon(ImageSource source)
    {
        if (source == null)
        {
            _image.Source = null;
            _image.Visibility = Visibility.Collapsed;
            _letter.Visibility = Visibility.Visible;
            return;
        }
        _image.Source = source;
        _image.Visibility = Visibility.Visible;
        _letter.Visibility = Visibility.Collapsed;
    }

    private void Scale(double to)
    {
        if (RenderTransform is ScaleTransform st)
        {
            st.BeginAnimation(ScaleTransform.ScaleXProperty, Theme.Anim(to, 120));
            st.BeginAnimation(ScaleTransform.ScaleYProperty, Theme.Anim(to, 120));
        }
    }

    public void SetEditing(bool editing)
    {
        _editing = editing;
        _deleteBadge.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        Background = Brushes.Transparent;
        Cursor = editing ? Cursors.SizeAll : Cursors.Hand;
        _label.Opacity = editing ? 0.8 : 1;
    }

    public void Highlight(bool on)
        => Background = on ? Theme.Frozen(0x33, 0x2B, 0xE0, 0xC8) : Brushes.Transparent;

    private void ShowMenu()
    {
        bool url = IsUrl(Item.Target);
        bool exists = url || System.IO.Directory.Exists(Item.Target) || System.IO.File.Exists(Item.Target);

        var menu = new MiniMenu().Add("打开", () => LaunchRequested?.Invoke(Item));

        if (!url && exists && System.IO.File.Exists(Item.Target))
            menu.Add("在文件夹中显示", () => RevealRequested?.Invoke(Item));

        menu.Add("编辑…", () => EditRequested?.Invoke(Item))
            .AddSeparator()
            .Add("移除", () => DeleteRequested?.Invoke(Item))
            .Show(this);
    }
}
