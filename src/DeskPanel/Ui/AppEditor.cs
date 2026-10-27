using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace DeskPanel.Ui;

/// <summary>编辑一格：名称 / 目标 / 参数 / 自定义图标。</summary>
internal sealed class AppEditor : DialogShell
{
    public AppItem Result { get; }

    private readonly TextBox _name = Kit.Input();
    private readonly TextBox _target = Kit.Input();
    private readonly TextBox _args = Kit.Input();
    private readonly TextBox _icon = Kit.Input();

    public AppEditor(AppItem src) : base("编辑", 480, 420)
    {
        Result = new AppItem
        {
            Name = src.Name,
            Target = src.Target,
            Args = src.Args,
            Icon = src.Icon,
        };

        _name.Text = src.Name;
        _target.Text = src.Target;
        _args.Text = src.Args;
        _icon.Text = src.Icon;

        var rows = new Grid();
        for (int i = 0; i < 5; i++) rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        rows.Children.Add(Stack("名称", _name, 0));

        var targetRow = Stack("目标（程序 / 快捷方式 / 文件夹 / 网址）", _target, 1);
        var targetGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        targetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        targetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var targetField = Kit.Field(_target);
        targetGrid.Children.Add(targetField);

        var browse = Kit.Ghost("浏览…", () =>
        {
            var dlg = new OpenFileDialog
            {
                Filter = "程序与快捷方式|*.exe;*.lnk;*.url;*.bat;*.cmd|所有文件|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog() == true)
            {
                _target.Text = dlg.FileName;
                if (string.IsNullOrWhiteSpace(_name.Text))
                    _name.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        });
        browse.VerticalAlignment = VerticalAlignment.Bottom;
        browse.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(browse, 1);
        targetGrid.Children.Add(browse);
        Grid.SetRow(targetGrid, 1);
        rows.Children.Add(targetGrid);

        rows.Children.Add(Stack("启动参数（可空）", _args, 2));

        var iconGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        iconGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        iconGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        iconGrid.Children.Add(Stack("图标（可空，留空自动取）", _icon, 0));
        var pick = Kit.Ghost("选择…", () =>
        {
            var dlg = new OpenFileDialog { Filter = "图标|*.ico;*.png;*.exe;*.lnk|所有文件|*.*", CheckFileExists = true };
            if (dlg.ShowDialog() == true) _icon.Text = dlg.FileName;
        });
        pick.VerticalAlignment = VerticalAlignment.Bottom;
        pick.Margin = new Thickness(8, 0, 12, 0);
        Grid.SetColumn(pick, 1);
        iconGrid.Children.Add(pick);
        Grid.SetRow(iconGrid, 3);
        rows.Children.Add(iconGrid);

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
        Grid.SetRow(bar, 5);
        rows.Children.Add(bar);

        Body.Children.Add(rows);
        Loaded += (_, _) => _name.Focus();
    }

    private static UIElement Stack(string label, TextBox box, int row)
    {
        var panel = new StackPanel { Margin = new Thickness(0, row == 0 ? 0 : 12, 0, 0) };
        panel.Children.Add(Kit.Label(label));
        panel.Children.Add(Kit.Field(box));
        Grid.SetRow(panel, row);
        return panel;
    }

    private void Accept()
    {
        Result.Name = string.IsNullOrWhiteSpace(_name.Text) ? "未命名" : _name.Text.Trim();
        Result.Target = _target.Text.Trim();
        Result.Args = _args.Text.Trim();
        Result.Icon = _icon.Text.Trim();
        DialogResult = true;
        Close();
    }
}
