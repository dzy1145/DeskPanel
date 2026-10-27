using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace DeskPanel.Ui;

/// <summary>新增 / 编辑一条服务：名字 + 启动命令 + 进程名（用来判断在跑）。</summary>
internal sealed class ServiceEditor : DialogShell
{
    public ServiceItem Result { get; }

    private readonly TextBox _name = Kit.Input();
    private readonly TextBox _command = Kit.Input();
    private readonly TextBox _args = Kit.Input();
    private readonly TextBox _workDir = Kit.Input();
    private readonly TextBox _processes = Kit.Input();
    private readonly TextBox _stopMatch = Kit.Input();

    public ServiceEditor(ServiceItem src) : base("服务", 520, 560)
    {
        Result = new ServiceItem
        {
            Name = src.Name,
            StartCommand = src.StartCommand,
            StartArgs = src.StartArgs,
            WorkingDir = src.WorkingDir,
            ProcessNames = new List<string>(src.ProcessNames),
            StopMatch = src.StopMatch,
        };

        _name.Text = src.Name;
        _command.Text = src.StartCommand;
        _args.Text = src.StartArgs;
        _workDir.Text = src.WorkingDir;
        _processes.Text = string.Join(", ", src.ProcessNames);
        _stopMatch.Text = src.StopMatch;

        var rows = new Grid();
        for (int i = 0; i < 7; i++) rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        rows.Children.Add(Field("名称", _name, 0));

        // 启动命令 + 浏览
        var commandGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        commandGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        commandGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        commandGrid.Children.Add(Field("启动命令（exe / bat / cmd / ps1）", _command, 0));
        var browse = Kit.Ghost("浏览…", () =>
        {
            var dlg = new OpenFileDialog
            {
                Filter = "可执行与脚本|*.exe;*.bat;*.cmd;*.ps1|所有文件|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog() == true)
            {
                _command.Text = dlg.FileName;
                if (string.IsNullOrWhiteSpace(_name.Text))
                    _name.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
                if (string.IsNullOrWhiteSpace(_workDir.Text))
                    _workDir.Text = Path.GetDirectoryName(dlg.FileName) ?? "";
            }
        });
        browse.VerticalAlignment = VerticalAlignment.Bottom;
        browse.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(browse, 1);
        commandGrid.Children.Add(browse);
        Grid.SetRow(commandGrid, 1);
        rows.Children.Add(commandGrid);

        rows.Children.Add(Field("启动参数（可空）", _args, 2));

        // 工作目录 + 选择
        var dirGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        dirGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dirGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        dirGrid.Children.Add(Field("工作目录（可空，默认取命令所在目录）", _workDir, 0));
        var pickDir = Kit.Ghost("选择…", () =>
        {
            var picked = FolderPicker.Pick("选择工作目录");
            if (!string.IsNullOrEmpty(picked)) _workDir.Text = picked;
        });
        pickDir.VerticalAlignment = VerticalAlignment.Bottom;
        pickDir.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(pickDir, 1);
        dirGrid.Children.Add(pickDir);
        Grid.SetRow(dirGrid, 3);
        rows.Children.Add(dirGrid);

        rows.Children.Add(Field("进程名（逗号分隔，例如 python, node —— 用来判断是否在跑）", _processes, 4));
        rows.Children.Add(Field("停止时额外匹配的命令行关键字（可空，例如 main.py）", _stopMatch, 5));

        var hint = new TextBlock
        {
            Text = "点 ▶ 启动这条命令；点 ■ 会结束匹配到的进程。",
            FontFamily = Theme.Ui,
            FontSize = 10.5,
            Foreground = Theme.Faint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0),
        };
        Grid.SetRow(hint, 6);
        rows.Children.Add(hint);

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 20, 0, 0),
        };
        var cancel = Kit.Button("取消", false, Close);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        bar.Children.Add(cancel);
        bar.Children.Add(Kit.Button("保存", true, Accept));
        Grid.SetRow(bar, 7);
        rows.Children.Add(bar);

        Body.Children.Add(rows);
        Loaded += (_, _) => _name.Focus();
    }

    private static UIElement Field(string label, TextBox box, int row)
    {
        var panel = new StackPanel { Margin = new Thickness(0, row == 0 ? 0 : 12, 0, 0) };
        panel.Children.Add(Kit.Label(label));
        panel.Children.Add(Kit.Field(box));
        Grid.SetRow(panel, row);
        return panel;
    }

    private void Accept()
    {
        Result.Name = string.IsNullOrWhiteSpace(_name.Text) ? "服务" : _name.Text.Trim();
        Result.StartCommand = _command.Text.Trim();
        Result.StartArgs = _args.Text.Trim();
        Result.WorkingDir = _workDir.Text.Trim();
        Result.StopMatch = _stopMatch.Text.Trim();
        Result.ProcessNames = _processes.Text
            .Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        DialogResult = true;
        Close();
    }
}
