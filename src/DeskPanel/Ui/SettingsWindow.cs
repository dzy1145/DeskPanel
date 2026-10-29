using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DeskPanel.Ui;

/// <summary>
/// 可视化设置：左侧分区导航 + 右侧卡片式表单。
/// 改动先写进草稿 <see cref="_draft"/>，点「保存」才落盘并让主窗口重载。
/// </summary>
internal sealed class SettingsWindow : Window
{
    private readonly MainWindow _main;
    private Config _draft;

    private readonly ContentControl _pageHost = new() { Margin = new Thickness(20, 6, 18, 8) };
    private readonly List<(string id, Border host, TextBlock label)> _nav = new();
    private readonly Dictionary<string, Func<FrameworkElement>> _pages = new();
    private string _pageId = "appearance";

    public SettingsWindow(MainWindow main, Config current)
    {
        _main = main;
        _draft = Clone(current);

        Title = "DeskPanel 设置";
        Width = 800;
        Height = 620;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Theme.Ui;

        _pages["appearance"] = BuildAppearance;
        _pages["monitor"] = BuildMonitor;
        _pages["launcher"] = BuildLauncher;
        _pages["services"] = BuildServices;
        _pages["usage"] = BuildUsage;
        _pages["about"] = BuildAbout;

        Content = BuildShell();
        ShowPage("appearance", animate: false);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    private static Config Clone(Config cfg)
        => JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(cfg, Config.JsonOptions), Config.JsonOptions)
           ?? new Config();

    // ---------------------------------------------------------------- 外壳

    private FrameworkElement BuildShell()
    {
        var card = new Border
        {
            Background = Theme.DialogBg,
            BorderBrush = Theme.HairStrong,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            ClipToBounds = true,
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });

        root.Children.Add(BuildTitleBar());

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(172) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        body.Children.Add(BuildNav());

        var line = new Border { Background = Theme.Hair, Width = 1 };
        Grid.SetColumn(line, 1);
        body.Children.Add(line);

        var scroll = new ScrollViewer
        {
            Content = _pageHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetColumn(scroll, 2);
        body.Children.Add(scroll);

        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = BuildFooter();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        var hair = new Border { Height = 1, Background = Theme.Hair, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetRow(hair, 0);
        root.Children.Add(hair);

        card.Child = root;
        return card;
    }

    private FrameworkElement BuildTitleBar()
    {
        var grid = new Grid { Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "设置",
            FontFamily = Theme.Ui,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 0, 0),
        };
        grid.Children.Add(title);

        var close = new Border
        {
            Width = 30,
            Height = 30,
            Margin = new Thickness(0, 0, 16, 0),
            CornerRadius = new CornerRadius(9),
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
        grid.Children.Add(close);

        grid.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        return grid;
    }

    private FrameworkElement BuildNav()
    {
        var panel = new StackPanel { Margin = new Thickness(10, 14, 10, 10) };

        AddNav(panel, "appearance", "外观");
        AddNav(panel, "monitor", "监控");
        AddNav(panel, "launcher", "常用");
        AddNav(panel, "services", "服务");
        AddNav(panel, "usage", "额度");
        AddNav(panel, "about", "关于");
        return panel;
    }

    private void AddNav(StackPanel host, string id, string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontFamily = Theme.Ui,
            FontSize = 12.5,
            Foreground = Theme.Dim,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        var item = new Border
        {
            Height = 36,
            CornerRadius = new CornerRadius(9),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 1, 0, 1),
            Child = label,
        };
        item.MouseEnter += (_, _) =>
        {
            if (_pageId != id) item.Background = Theme.Hover;
        };
        item.MouseLeave += (_, _) =>
        {
            if (_pageId != id) item.Background = Brushes.Transparent;
        };
        item.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowPage(id, animate: true);
        };

        host.Children.Add(item);
        _nav.Add((id, item, label));
    }

    private FrameworkElement BuildFooter()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var hint = new TextBlock
        {
            Text = "改动会写入 config.json",
            FontFamily = Theme.Ui,
            FontSize = 10.5,
            Foreground = Theme.Faint,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        grid.Children.Add(hint);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 18, 0),
        };
        var cancel = Kit.Button("取消", false, Close);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(Kit.Button("保存", true, Save));
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        var host = new Grid();
        host.Children.Add(grid);
        var hair = new Border { Height = 1, Background = Theme.Hair, VerticalAlignment = VerticalAlignment.Top };
        host.Children.Add(hair);
        return host;
    }

    private void ShowPage(string id, bool animate)
    {
        _pageId = id;

        foreach (var (nid, host, label) in _nav)
        {
            bool on = nid == id;
            host.Background = on ? Theme.Frozen(0x1A, 0xFF, 0xFF, 0xFF) : Brushes.Transparent;
            label.Foreground = on ? Theme.Text : Theme.Dim;
            label.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }

        if (!_pages.TryGetValue(id, out var builder)) return;

        var page = builder();
        _pageHost.Content = page;
        if (animate) Widgets.SlideIn(page);
    }

    // ---------------------------------------------------------------- 外观

    private FrameworkElement BuildAppearance()
    {
        var widthStepper = new Widgets.Stepper(_draft.Width, 220, 420, 20);
        widthStepper.Changed += v => _draft.Width = v;

        var theme = new Widgets.Segmented(_draft.Theme,
            ("ink", "深色半透明"), ("solid", "纯色"));
        theme.Changed += v => _draft.Theme = v;

        var clock = new Widgets.Switch(_draft.ShowClock);
        clock.Changed += v => _draft.ShowClock = v;

        var seconds = new Widgets.Switch(_draft.ClockSeconds > 0);
        seconds.Changed += v => _draft.ClockSeconds = v ? 1 : 0;

        var layer = new Widgets.Switch(_draft.DesktopLayer);
        layer.Changed += v => _draft.DesktopLayer = v;

        var noActivate = new Widgets.Switch(_draft.NoActivate);
        noActivate.Changed += v => _draft.NoActivate = v;

        var acrylic = new Widgets.Switch(_draft.Acrylic);
        acrylic.Changed += v => _draft.Acrylic = v;

        return new StackPanel
        {
            Children =
            {
                SectionTitle("外观", "面板尺寸、主题与桌面层行为"),
                Widgets.Card(
                    Widgets.Row("宽度", "DIP；实际会按屏宽收敛到工作区的 30% 以内", widthStepper),
                    Widgets.Row("主题", "ink = 半透明深色；solid = 纯色不透明", theme),
                    Widgets.Row("显示时钟", null, clock),
                    Widgets.Row("时钟显示秒", null, seconds),
                    Widgets.Row("亚克力模糊", "分层窗口不吃该效果，通常保持关闭", acrylic),
                    Widgets.Row("贴桌面层", "关闭后变成普通置顶窗口（不会只在 Win+D 后出现）", layer),
                    Widgets.Row("点击不抢焦点", "开启后可能吞掉部分鼠标输入", noActivate, divider: false)),

                SectionTitle("提示", null, 16),
                Text("宽度、刷新间隔、常用列数、服务、额度会立即生效；主题、亚克力、贴桌面层、点击不抢焦点与盘符需要重启一次面板。",
                     Theme.Faint, 10.5),
            },
        };
    }

    // ---------------------------------------------------------------- 监控

    private FrameworkElement BuildMonitor()
    {
        var refresh = new Widgets.Stepper(_draft.RefreshMs, 500, 5000, 500, " ms");
        refresh.Changed += v => _draft.RefreshMs = v;

        var chips = new StackPanel { Orientation = Orientation.Horizontal };
        var fixedDrives = Config.DetectDrivesAll();

        foreach (var drive in fixedDrives)
        {
            var chip = new Widgets.Chip(drive, _draft.Drives.Contains(drive, StringComparer.OrdinalIgnoreCase));
            chip.Changed += _ =>
            {
                var selected = chips.Children.OfType<Widgets.Chip>().Where(c => c.IsOn).Select(c => c.Key).ToList();
                if (selected.Count == 0)
                {
                    // 不允许一块都不选：把刚点的这块勾回去，免得 UI 和配置不一致
                    chip.IsOn = true;
                    return;
                }
                _draft.Drives = selected;
            };
            chips.Children.Add(chip);
        }

        return new StackPanel
        {
            Children =
            {
                SectionTitle("监控", "采样频率与要显示的磁盘"),
                Widgets.Card(
                    Widgets.Row("刷新间隔", "越小越跟手，越费 CPU；默认 1000 ms", refresh),
                    Widgets.Row("监控磁盘", "只显示选中的固定盘（改动需重启面板生效）", chips, divider: false)),

                SectionTitle("说明", null, 16),
                Text("CPU 温度来自 ACPI 热区（PDH），GPU 温度/功耗来自 NVIDIA NVML；都不需要管理员权限。\n" +
                     "没有 NVIDIA 显卡时 GPU 温度会显示 --，这是正常降级。", Theme.Faint, 10.5),
            },
        };
    }

    // ---------------------------------------------------------------- 常用

    private FrameworkElement BuildLauncher()
    {
        var columns = new Widgets.Stepper(_draft.Launcher.Columns, 2, 8, 1);
        columns.Changed += v => _draft.Launcher.Columns = v;

        var rows = new Widgets.Stepper(_draft.Launcher.MaxRows, 1, 8, 1);
        rows.Changed += v => _draft.Launcher.MaxRows = v;

        var count = new TextBlock
        {
            Text = $"{_draft.Launcher.Apps.Count} 个磁贴",
            FontFamily = Theme.Num,
            FontSize = 12.5,
            Foreground = Theme.Dim,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var clear = Kit.Ghost("清空常用", () =>
        {
            if (_draft.Launcher.Apps.Count == 0) return;
            var dialog = new ConfirmDialog("清空常用", "确定清空常用区里的所有磁贴？", "清空");
            if (dialog.ShowDialog() != true) return;
            _draft.Launcher.Apps.Clear();
            ShowPage("launcher", animate: true);
        });

        return new StackPanel
        {
            Children =
            {
                SectionTitle("常用", "磁贴的列数与可见行数"),
                Widgets.Card(
                    Widgets.Row("列数", "每行放几个磁贴", columns),
                    Widgets.Row("可见行数", "超出后在面板内部滚动，不显示滚动条", rows),
                    Widgets.Row("磁贴数量", "在面板上拖入程序 / 快捷方式 / 文件夹 / 网址即可添加", count, divider: false)),

                SectionTitle("维护", null, 16),
                Widgets.Card(clear),

                Text("提示：拖动排序在面板的「编辑」模式下可用；这里只调整网格尺寸。", Theme.Faint, 10.5),
            },
        };
    }

    // ---------------------------------------------------------------- 服务

    private FrameworkElement BuildServices()
    {
        var list = new StackPanel();

        if (_draft.Services.Count == 0)
        {
            list.Children.Add(Text("还没有服务。点下面的「新增服务」加一条。", Theme.Faint, 11));
        }

        for (int i = 0; i < _draft.Services.Count; i++)
        {
            int index = i;
            var item = _draft.Services[i];

            var name = new TextBlock
            {
                Text = item.Name,
                FontFamily = Theme.Ui,
                FontSize = 12.5,
                Foreground = Theme.Text,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var command = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(item.StartCommand) ? "（未填写启动命令）" : item.StartCommand,
                FontFamily = Theme.Mono,
                FontSize = 10,
                Foreground = Theme.Faint,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0),
            };
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(name);
            texts.Children.Add(command);

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var edit = Kit.Ghost("编辑", () =>
            {
                var editor = new ServiceEditor(_draft.Services[index]);
                if (editor.ShowDialog() != true) return;
                _draft.Services[index] = editor.Result;
                ShowPage("services", animate: false);
            });
            edit.Margin = new Thickness(0, 0, 4, 0);
            var remove = Kit.Ghost("删除", () =>
            {
                var confirm = new ConfirmDialog("删除服务", "把「" + item.Name + "」从服务列表移除？\n\n不会结束正在运行的进程。", "删除");
                if (confirm.ShowDialog() != true) return;
                _draft.Services.RemoveAt(index);
                ShowPage("services", animate: false);
            });
            actions.Children.Add(edit);
            actions.Children.Add(remove);

            var grid = new Grid { MinHeight = 44 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(texts);
            Grid.SetColumn(actions, 1);
            actions.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(actions);

            var host = new Grid();
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.Children.Add(grid);
            if (index < _draft.Services.Count - 1)
            {
                var hair = new Border { Height = 1, Background = Theme.Hair, Margin = new Thickness(0, 2, 0, 2) };
                Grid.SetRow(hair, 1);
                host.Children.Add(hair);
            }
            list.Children.Add(host);
        }

        var add = Kit.Button("新增服务", true, () =>
        {
            var editor = new ServiceEditor(new ServiceItem());
            if (editor.ShowDialog() != true) return;
            _draft.Services.Add(editor.Result);
            ShowPage("services", animate: false);
        });
        add.HorizontalAlignment = HorizontalAlignment.Left;

        return new StackPanel
        {
            Children =
            {
                SectionTitle("服务", "启动命令 + 进程名；面板上点 ▶ 启动、点 ■ 停止"),
                Widgets.Card(list),
                add,
                Text("进程名决定“在跑”的判断（不带 .exe）；停止匹配关键字可选，用来区分同名进程。\n" +
                     "关键字只对同架构、同权限的进程有效。", Theme.Faint, 10.5).Also(t => t.Margin = new Thickness(0, 10, 0, 0)),
            },
        };
    }

    // ---------------------------------------------------------------- 额度

    private FrameworkElement BuildUsage()
    {
        var enabled = new Widgets.Switch(_draft.Usage.Enabled);
        var mode = new Widgets.Segmented(_draft.Usage.Mode,
            ("go", "OpenCode Go"), ("deepseek", "DeepSeek 余额"));
        var (openCodeHost, openCodeKey) = Kit.Secret(_draft.Usage.OpenCodeKey);
        var (deepSeekHost, deepSeekKey) = Kit.Secret(_draft.Usage.DeepSeekKey);

        enabled.Changed += v => _draft.Usage.Enabled = v;
        mode.Changed += v => _draft.Usage.Mode = v;
        openCodeKey.PasswordChanged += (_, _) => _draft.Usage.OpenCodeKey = openCodeKey.Password.Trim();
        deepSeekKey.PasswordChanged += (_, _) => _draft.Usage.DeepSeekKey = deepSeekKey.Password.Trim();

        return new StackPanel
        {
            Children =
            {
                SectionTitle("额度", "可选；关闭时整块隐藏且不联网"),
                Widgets.Card(
                    Widgets.Row("显示额度区", "打开后每分钟拉一次数据", enabled),
                    Widgets.Row("默认视图", "点面板上的「额度」可以在两种视图之间切换", mode),
                    Widgets.Row("OpenCode Go", "key 留空则读环境变量 OPENCODE_GO_API_KEY", openCodeHost),
                    Widgets.Row("DeepSeek", "key 留空则读环境变量 DEEPSEEK_API_KEY", deepSeekHost, divider: false)),

                Text("key 只保存在本机 config.json，不会上传；面板只直连官方接口。", Theme.Faint, 10.5),
            },
        };
    }

    // ---------------------------------------------------------------- 关于

    private FrameworkElement BuildAbout()
    {
        string version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        var autostart = new Widgets.Switch(Tray.IsAutostart());
        autostart.Changed += v => Tray.SetAutostart(v);

        return new StackPanel
        {
            Children =
            {
                SectionTitle("关于", "DeskPanel · Windows 桌面侧栏"),
                Widgets.Card(
                    Widgets.Row("版本", _main.UpdateStatusText, new TextBlock
                    {
                        Text = "v" + version,
                        FontFamily = Theme.Num,
                        FontSize = 12.5,
                        Foreground = Theme.Text,
                        VerticalAlignment = VerticalAlignment.Center,
                    }),
                    Widgets.Row("自动检测更新", null, AutoUpdateSwitch()),
                    Widgets.Row("检测间隔", "小时；默认 6", AutoUpdateHours()),
                    Widgets.Row("检查更新", null, Kit.Ghost(_main.HasUpdate ? "去下载" : "检查", async () =>
                    {
                        if (_main.HasUpdate)
                        {
                            _main.OfferUpdateForLatest();
                            return;
                        }
                        await _main.CheckUpdateNow();
                        ShowPage("about", animate: false);
                    })),
                    Widgets.Row("开机自启", "写入 HKCU 的 Run 项", autostart),
                    Widgets.Row("配置文件", "config.json（在 %LOCALAPPDATA%\\DeskPanel 下）", Link("打开", () => OpenPath(Config.FilePath))),
                    Widgets.Row("配置目录", null, Link("浏览", () => OpenPath(Config.Dir))),
                    Widgets.Row("日志", "排查问题时看这个", Link("打开", () => OpenPath(Path.Combine(Config.Dir, "app.log"))), divider: false)),

                SectionTitle("快捷键与入口", null, 16),
                Text("· 托盘图标：左键 / 右键都能打开菜单（设置、宽度、重新载入配置、退出）\n" +
                     "· Win+D：回到桌面就能看到面板\n" +
                     "· 面板「常用」区：把程序拖进去即可添加", Theme.Faint, 10.5),
            },
        };
    }

    private static FrameworkElement Link(string text, Action onClick) => Kit.Ghost(text, onClick);

    private Widgets.Switch AutoUpdateSwitch()
    {
        var toggle = new Widgets.Switch(_draft.Update != null && _draft.Update.Enabled);
        toggle.Changed += v =>
        {
            _draft.Update ??= new UpdateCfg();
            _draft.Update.Enabled = v;
        };
        return toggle;
    }

    private Widgets.Stepper AutoUpdateHours()
    {
        int hours = _draft.Update?.CheckHours ?? 6;
        var stepper = new Widgets.Stepper(hours, 1, 24, 1, " h");
        stepper.Changed += v =>
        {
            _draft.Update ??= new UpdateCfg();
            _draft.Update.CheckHours = v;
        };
        return stepper;
    }

    private static void OpenPath(string path)
    {
        try
        {
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(path)) new Config().Save();
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
                return;
            }
            if (File.Exists(path) || Directory.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }

    // ---------------------------------------------------------------- 小工具

    private static FrameworkElement SectionTitle(string title, string desc, double topMargin = 0)
    {
        var panel = new StackPanel { Margin = new Thickness(0, topMargin, 0, 10) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = Theme.Ui,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
        });
        if (!string.IsNullOrWhiteSpace(desc))
        {
            panel.Children.Add(new TextBlock
            {
                Text = desc,
                FontFamily = Theme.Ui,
                FontSize = 10.5,
                Foreground = Theme.Faint,
                Margin = new Thickness(0, 3, 0, 0),
            });
        }
        return panel;
    }

    private static TextBlock Text(string text, Brush brush, double size) => new()
    {
        Text = text,
        FontFamily = Theme.Ui,
        FontSize = size,
        Foreground = brush,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = size * 1.6,
    };

    // ---------------------------------------------------------------- 保存

    private void Save()
    {
        try
        {
            _main.ApplySettings(_draft);   // 里面会写盘并同步到运行中的面板
            Close();
        }
        catch (Exception ex)
        {
            Log.Ex(ex);
        }
    }
}

internal static class ElementExt
{
    /// <summary>链式设一下属性，省几行局部变量。</summary>
    public static T Also<T>(this T element, Action<T> action)
    {
        action(element);
        return element;
    }
}
