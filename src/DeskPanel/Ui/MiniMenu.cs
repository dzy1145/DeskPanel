using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DeskPanel.Ui;

/// <summary>深色小菜单：不继承系统 ContextMenu 的白色图标槽。</summary>
internal sealed class MiniMenu
{
    private readonly Popup _popup;
    private readonly StackPanel _rows = new();

    public MiniMenu()
    {
        var border = new Border
        {
            Background = Theme.Frozen(0xF7, 0x16, 0x17, 0x1C),
            BorderBrush = Theme.Frozen(0x2E, 0xFF, 0xFF, 0xFF),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = _rows,
        };

        _popup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = false,
            Child = border,
            Placement = PlacementMode.MousePoint,
            VerticalOffset = 4,
            HorizontalOffset = 4,
        };
    }

    public MiniMenu Add(string text, Action action)
    {
        var label = new TextBlock
        {
            Text = text,
            FontFamily = Theme.Ui,
            FontSize = 12,
            Foreground = Theme.Text,
        };

        var row = new Border
        {
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10, 6, 22, 6),
            Background = Brushes.Transparent,
            Child = label,
            Cursor = Cursors.Hand,
        };

        row.MouseEnter += (_, _) => row.Background = Theme.Hover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Close();
            action();
        };

        _rows.Children.Add(row);
        return this;
    }

    public MiniMenu AddSeparator()
    {
        _rows.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(6, 4, 6, 4),
            Background = Theme.Hair,
        });
        return this;
    }

    public void Show(UIElement target, PlacementMode placement = PlacementMode.MousePoint)
    {
        _popup.PlacementTarget = target;
        _popup.Placement = placement;
        _popup.IsOpen = true;
    }

    public void Close() => _popup.IsOpen = false;
}
