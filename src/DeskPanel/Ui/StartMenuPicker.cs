using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DeskPanel.Ui;

/// <summary>从开始菜单挑一个程序加进常用区。</summary>
internal sealed class StartMenuPicker : DialogShell
{
    private sealed class Entry
    {
        public string Name = "";
        public string Target = "";
    }

    private readonly List<Entry> _all = new();
    private readonly ListBox _list = new();
    private readonly TextBox _search = Kit.Input();

    public AppItem Result { get; private set; }

    public StartMenuPicker() : base("从开始菜单添加", 520, 620)
    {
        var box = new StackPanel();

        box.Children.Add(Kit.Field(_search));
        _search.TextChanged += (_, _) => ApplyFilter();

        _list.FontFamily = Theme.Ui;
        _list.FontSize = 12;
        _list.Background = Brushes.Transparent;
        _list.Foreground = Theme.Text;
        _list.BorderThickness = new Thickness(0);
        _list.Margin = new Thickness(0, 12, 0, 0);
        _list.MaxHeight = 400;
        _list.MouseDoubleClick += (_, _) => Accept();
        box.Children.Add(_list);

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var cancel = Kit.Button("取消", false, Close);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        bar.Children.Add(cancel);
        bar.Children.Add(Kit.Button("添加", true, Accept));
        box.Children.Add(bar);

        Body.Children.Add(box);

        Loaded += (_, _) =>
        {
            Mouse.OverrideCursor = Cursors.Wait;
            try { Scan(); }
            finally { Mouse.OverrideCursor = null; }
            ApplyFilter();
        };
    }

    private void Scan()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var lnk in ShortcutResolver.AllLnkFiles())
        {
            string target = ShortcutResolver.ResolveLnk(lnk);
            if (string.IsNullOrWhiteSpace(target) || !File.Exists(target)) continue;
            if (!seen.Add(target)) continue;

            _all.Add(new Entry { Name = Path.GetFileNameWithoutExtension(lnk), Target = target });
        }

        _all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
    }

    private void ApplyFilter()
    {
        string q = _search.Text.Trim();
        _list.Items.Clear();

        foreach (var e in _all)
        {
            if (q.Length > 0 &&
                e.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                e.Target.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var stack = new StackPanel { Margin = new Thickness(4, 6, 4, 6) };
            stack.Children.Add(new TextBlock
            {
                Text = e.Name,
                FontFamily = Theme.Ui,
                FontSize = 12.5,
                Foreground = Theme.Text,
            });
            stack.Children.Add(new TextBlock
            {
                Text = e.Target,
                FontFamily = Theme.Mono,
                FontSize = 9.5,
                Foreground = Theme.Faint,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 430,
            });

            var item = new ListBoxItem
            {
                Content = stack,
                Tag = e,
                Background = Brushes.Transparent,
                Padding = new Thickness(6, 0, 6, 0),
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            item.MouseEnter += (_, _) => item.Background = Theme.Hover;
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;

            _list.Items.Add(item);
        }

        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    private void Accept()
    {
        if (_list.SelectedItem is not ListBoxItem lbi || lbi.Tag is not Entry entry) return;

        Result = new AppItem { Name = entry.Name, Target = entry.Target };
        DialogResult = true;
        Close();
    }
}
