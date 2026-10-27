using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DeskPanel.Ui;

/// <summary>常用区：平时点即启动；编辑态可删除 / 拖拽排序（跟手浮起 + 其余格子避让）/ 添加。</summary>
internal sealed class AppPanel : StackPanel
{
    private readonly Config _cfg;
    private readonly Action<string> _notify;
    private readonly TileBoard _board = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _editText;
    private readonly Border _undoBtn;

    private bool _editing;
    private AppItem _undoItem;
    private int _undoIndex = -1;
    private readonly DispatcherTimer _undoTimer;

    private AppTile _pressTile;
    private Point _pressPoint;
    private bool _pressed;
    private bool _dragging;
    private AppTile _dragTile;
    private int _dragIndex;
    private Vector _grabOffset;
    private TranslateTransform _dragTranslate;

    public AppPanel(Config cfg, Action<string> notify)
    {
        _cfg = cfg;
        _notify = notify;
        Orientation = Orientation.Vertical;

        // ---- 头部 ----
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

        _editText = new TextBlock
        {
            Text = "编辑",
            FontFamily = Theme.Ui,
            FontSize = 10,
            Foreground = Theme.Faint,
        };
        var editBtn = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2, 7, 2),
            Background = Brushes.Transparent,
            Child = _editText,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        editBtn.MouseEnter += (_, _) => editBtn.Background = Theme.Hover;
        editBtn.MouseLeave += (_, _) => editBtn.Background = Brushes.Transparent;
        editBtn.MouseLeftButtonUp += (_, e) => { e.Handled = true; ToggleEdit(); };
        DockPanel.SetDock(editBtn, Dock.Right);
        header.Children.Add(editBtn);

        var title = new TextBlock
        {
            Text = "常用",
            FontFamily = Theme.Ui,
            FontSize = 10,
            Foreground = Theme.Faint,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _undoBtn = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(6, 0, 0, 0),
            Background = Theme.Hover,
            Visibility = Visibility.Collapsed,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "撤销移除",
                FontFamily = Theme.Ui,
                FontSize = 10,
                Foreground = Theme.Accent,
            },
        };
        _undoBtn.MouseLeftButtonUp += (_, e) => { e.Handled = true; Undo(); };

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(title);
        titleRow.Children.Add(_undoBtn);
        header.Children.Add(titleRow);

        Children.Add(header);

        // ---- 网格 ----
        _board.Columns = Math.Max(1, _cfg.Launcher.Columns);
        _board.ClipToBounds = false;
        _board.MouseMove += OnBoardMouseMove;
        _board.MouseLeftButtonUp += (_, _) => EndDrag();
        _board.LostMouseCapture += (_, _) => EndDrag();

        // 编辑态：接受从资源管理器 / 桌面拖进来的文件、文件夹（整块区域都收）
        _board.AllowDrop = true;
        _board.DragOver += OnFileDragOver;
        _board.DragLeave += OnFileDragLeave;
        _board.Drop += OnFileDrop;

        AllowDrop = true;
        DragOver += OnFileDragOver;
        DragLeave += OnFileDragLeave;
        Drop += OnFileDrop;

        _scroll = new ScrollViewer
        {
            Content = _board,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = Rows() * CellH() + 6,
        };
        Children.Add(_scroll);

        _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _undoTimer.Tick += (_, _) =>
        {
            _undoTimer.Stop();
            _undoItem = null;
            _undoIndex = -1;
            _undoBtn.Visibility = Visibility.Collapsed;
        };

        Rebuild();
    }

    // ------------------------------------------------------------ 构建

    /// <summary>常用区行数（配置）。行数越多格子越矮，好让下面的服务/启动项/额度都露出来。</summary>
    private int Rows() => Math.Max(1, _cfg.Launcher.MaxRows);

    /// <summary>每种行数对应的格子高度。</summary>
    private double CellH()
    {
        int rows = Rows();
        return rows <= 3 ? 60 : rows == 4 ? 54 : 48;
    }

    private void Rebuild()
    {
        _board.Children.Clear();
        _board.CellHeight = CellH();
        _scroll.ScrollToTop();

        foreach (var item in _cfg.Launcher.Apps.ToList())
        {
            var tile = new AppTile(item) { Height = CellH() - 4 };
            tile.LaunchRequested += Launch;
            tile.DeleteRequested += Remove;
            tile.EditRequested += Edit;
            tile.RevealRequested += Reveal;
            tile.SetEditing(_editing);
            if (_editing) WireDrag(tile);
            _board.Children.Add(tile);
        }

        if (_editing) _board.Children.Add(BuildAddTile());

        // 首次使用还没配常用软件：给一句提示，别留一大片空白
        if (_cfg.Launcher.Apps.Count == 0)
        {
            _board.Children.Add(new Border
            {
                Height = CellH() - 4,
                Child = new TextBlock
                {
                    Text = "点右上角「编辑」\n或把程序拖进来",
                    FontFamily = Theme.Ui,
                    FontSize = 10.5,
                    LineHeight = 16,
                    Foreground = Theme.Faint,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            _board.Relayout(false);
        }), DispatcherPriority.Loaded);
    }

    private Border BuildAddTile()
    {
        var add = new Border
        {
            Height = CellH() - 4,
            CornerRadius = new CornerRadius(12),
            BorderBrush = Theme.Frozen(0x33, 0xFF, 0xFF, 0xFF),
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent,
            Child = new TextBlock
            {
                Text = "+",
                FontFamily = Theme.Ui,
                FontSize = 20,
                Foreground = Theme.Faint,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Cursor = Cursors.Hand,
        };
        add.MouseEnter += (_, _) => add.Background = Theme.Hover;
        add.MouseLeave += (_, _) => add.Background = Brushes.Transparent;
        add.MouseLeftButtonUp += (_, e) => { e.Handled = true; ShowAddMenu(add); };
        return add;
    }

    private void ShowAddMenu(UIElement anchor)
    {
        new MiniMenu()
            .Add("从开始菜单选择…", PickFromStartMenu)
            .Add("浏览文件…", BrowseFile)
            .Add("浏览文件夹…", BrowseFolder)
            .Add("添加网址…", AddUrl)
            .Add("拖放窗口…", OpenDropPad)
            .Show(anchor, PlacementMode.Bottom);
    }

    /// <summary>开一个普通小窗专门接拖放（面板自己是桌面层窗口，shell 不一定投递）。</summary>
    private void OpenDropPad()
    {
        new DropPadWindow(paths => AcceptFiles(paths, new Point(0, 0))).Show();
        _notify?.Invoke("把东西拖到小窗里");
    }

    // ------------------------------------------------------------ 从外部拖入

    private bool _fileDragActive;

    /// <summary>窗口级拖放转交进来：paths + 屏幕坐标。返回是否收下。</summary>
    public bool AcceptFiles(string[] paths, Point screenPoint)
    {
        if (paths == null || paths.Length == 0) return false;

        Point local;
        try { local = _board.PointFromScreen(screenPoint); }
        catch { local = new Point(0, 0); }

        int index = Math.Min(_board.IndexAt(local), _cfg.Launcher.Apps.Count);
        int made = 0;

        foreach (var path in paths)
        {
            var item = ShortcutFactory.ToItem(path, out _);
            if (item == null) continue;

            if (string.IsNullOrWhiteSpace(item.Name)) item.Name = "未命名";
            if (item.Name.Length > 8) item.Name = item.Name[..8];

            var list = _cfg.Launcher.Apps;
            list.Insert(Math.Clamp(index, 0, list.Count), item);
            index++;
            made++;
        }

        if (made == 0)
        {
            Log.Write("drop: 拖进来的没有可用项");
            _notify?.Invoke("拖进来的东西没法用");
            return false;
        }

        Log.Write($"drop: 已添加 {made} 项");
        _cfg.Save();
        Rebuild();
        _notify?.Invoke(_editing ? $"已添加 {made} 项" : $"已添加 {made} 项（拖到编辑态可排序）");
        return true;
    }

    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Copy;

        if (!_fileDragActive)
        {
            _fileDragActive = true;
            Log.Write("drop: 外部拖拽进入面板");
            _notify?.Invoke("松手放到常用");
        }
        e.Handled = true;
    }

    private void OnFileDragLeave(object sender, DragEventArgs e)
    {
        _fileDragActive = false;
        e.Handled = true;
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        _fileDragActive = false;
        e.Handled = true;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

        Point screen;
        try { screen = PointToScreen(e.GetPosition(this)); }
        catch { screen = new Point(0, 0); }

        AcceptFiles(paths, screen);
    }

    // ------------------------------------------------------------ 拖动（跟手 + 避让）

    private void WireDrag(AppTile tile)
    {
        tile.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _pressTile = tile;
            _pressPoint = e.GetPosition(_board);
            _pressed = true;
        };

        tile.MouseMove += (_, e) =>
        {
            if (!_pressed || _dragging || e.LeftButton != MouseButtonState.Pressed) return;
            Point p = e.GetPosition(_board);
            if (Math.Abs(p.X - _pressPoint.X) < 5 && Math.Abs(p.Y - _pressPoint.Y) < 5) return;
            BeginDrag(tile, p);
        };

        tile.PreviewMouseLeftButtonUp += (_, _) => _pressed = false;
    }

    private void BeginDrag(AppTile tile, Point p)
    {
        _pressed = false;
        _dragging = true;
        _dragTile = tile;
        _dragIndex = _board.Children.IndexOf(tile);

        double left = Canvas.GetLeft(tile);
        double top = Canvas.GetTop(tile);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;

        _grabOffset = new Vector(p.X - left, p.Y - top);

        _dragTranslate = new TranslateTransform();
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(1.06, 1.06));
        group.Children.Add(_dragTranslate);

        tile.RenderTransformOrigin = new Point(0.5, 0.5);
        tile.RenderTransform = group;
        tile.Effect = new DropShadowEffect
        {
            BlurRadius = 16,
            ShadowDepth = 0,
            Opacity = 0.55,
            Color = Colors.Black,
        };
        tile.Opacity = 0.94;
        Panel.SetZIndex(tile, 100);

        _board.SkipAnimation = tile;
        _board.CaptureMouse();
        UpdateDrag(p);
    }

    private void OnBoardMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        UpdateDrag(e.GetPosition(_board));
    }

    private void UpdateDrag(Point p)
    {
        if (!_dragging || _dragTile == null || _dragTranslate == null) return;

        int idx = _board.IndexAt(p);
        int lastAppCell = Math.Max(0, _board.Children.Count - 2);   // "+" 永远在最后
        int target = Math.Min(idx, lastAppCell);

        if (target != _dragIndex)
        {
            _board.MoveChild(_dragIndex, target);

            var list = _cfg.Launcher.Apps;
            var item = _dragTile.Item;
            if (list.Remove(item))
                list.Insert(Math.Clamp(target, 0, list.Count), item);

            _dragIndex = target;
            _board.Relayout(true);
        }

        double left = Canvas.GetLeft(_dragTile);
        double top = Canvas.GetTop(_dragTile);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;

        _dragTranslate.X = p.X - _grabOffset.X - left;
        _dragTranslate.Y = p.Y - _grabOffset.Y - top;
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        _pressed = false;

        var tile = _dragTile;
        _dragTile = null;

        if (tile != null && _dragTranslate != null)
        {
            // 把「跟手位移」转成新起点，松手后用弹簧收进槽位
            double left = Canvas.GetLeft(tile) + _dragTranslate.X;
            double top = Canvas.GetTop(tile) + _dragTranslate.Y;

            tile.RenderTransform = Transform.Identity;
            tile.Effect = null;
            tile.Opacity = 1;
            Panel.SetZIndex(tile, 0);

            Canvas.SetLeft(tile, left);
            Canvas.SetTop(tile, top);

            _board.SkipAnimation = null;
            _board.Relayout(true);
        }

        _dragTranslate = null;
        if (_board.IsMouseCaptured) _board.ReleaseMouseCapture();

        _cfg.Save();
    }

    // ------------------------------------------------------------ 编辑态

    private void ToggleEdit()
    {
        _editing = !_editing;
        _editText.Text = _editing ? "完成" : "编辑";
        _editText.Foreground = _editing ? Theme.Accent : Theme.Faint;
        Rebuild();
        _notify?.Invoke(_editing ? "拖动可排序，× 可移除" : "");
    }

    // ------------------------------------------------------------ 动作

    private void Launch(AppItem item)
    {
        try
        {
            string target = item.Target?.Trim() ?? "";
            if (target.Length == 0)
            {
                _notify?.Invoke("未设置目标：" + item.Name);
                return;
            }

            bool url = target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                       target.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            if (!url && !File.Exists(target) && !Directory.Exists(target))
            {
                _notify?.Invoke("未找到：" + item.Name);
                return;
            }

            Process.Start(new ProcessStartInfo(target, item.Args ?? "")
            {
                UseShellExecute = true,
                WorkingDirectory = url || Directory.Exists(target)
                    ? ""
                    : System.IO.Path.GetDirectoryName(target) ?? "",
            });
        }
        catch (Exception ex)
        {
            _notify?.Invoke("启动失败：" + ex.Message);
        }
    }

    private void Reveal(AppItem item)
    {
        try
        {
            if (!File.Exists(item.Target)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Target}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private void Edit(AppItem item)
    {
        var dlg = new AppEditor(item);
        if (dlg.ShowDialog() != true) return;

        item.Name = dlg.Result.Name;
        item.Target = dlg.Result.Target;
        item.Args = dlg.Result.Args;
        item.Icon = dlg.Result.Icon;
        _cfg.Save();
        Rebuild();
    }

    private void Remove(AppItem item)
    {
        var list = _cfg.Launcher.Apps;
        int index = list.IndexOf(item);
        if (index < 0) return;

        _undoItem = item;
        _undoIndex = index;
        list.RemoveAt(index);
        _cfg.Save();
        Rebuild();

        _undoBtn.Visibility = Visibility.Visible;
        _undoTimer.Stop();
        _undoTimer.Start();
        _notify?.Invoke("已移除 " + item.Name);
    }

    private void Undo()
    {
        if (_undoItem == null) return;

        var list = _cfg.Launcher.Apps;
        int index = Math.Clamp(_undoIndex, 0, list.Count);
        list.Insert(index, _undoItem);
        _undoItem = null;
        _undoIndex = -1;
        _undoBtn.Visibility = Visibility.Collapsed;
        _undoTimer.Stop();

        _cfg.Save();
        Rebuild();
        _notify?.Invoke("已恢复");
    }

    private void PickFromStartMenu()
    {
        var picker = new StartMenuPicker();
        if (picker.ShowDialog() != true || picker.Result == null) return;
        Add(picker.Result);
    }

    private void BrowseFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择要添加的程序 / 快捷方式 / 文件",
            Filter = "程序与快捷方式|*.exe;*.lnk;*.url;*.bat;*.cmd|所有文件|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return;

        Add(new AppItem
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName),
            Target = dlg.FileName,
        });
    }

    private void BrowseFolder()
    {
        string dir = FolderPicker.Pick("选择要添加的文件夹");
        if (string.IsNullOrWhiteSpace(dir)) return;

        string name = System.IO.Path.GetFileName(dir.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(name)) name = dir;

        Add(new AppItem { Name = name, Target = dir });
    }

    private void AddUrl()
    {
        var dlg = new UrlDialog();
        if (dlg.ShowDialog() != true || dlg.Result == null) return;
        Add(dlg.Result);
    }

    private void Add(AppItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Name)) item.Name = "未命名";
        item.Name = item.Name.Length > 8 ? item.Name[..8] : item.Name;

        _cfg.Launcher.Apps.Add(item);
        _cfg.Save();
        Rebuild();
        _notify?.Invoke("已添加 " + item.Name);
    }
}
