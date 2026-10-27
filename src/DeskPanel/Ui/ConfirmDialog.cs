using System.Windows;
using System.Windows.Controls;

namespace DeskPanel.Ui;

/// <summary>危险操作的确认框（深色，跟其它对话框同一套）。</summary>
internal sealed class ConfirmDialog : DialogShell
{
    public ConfirmDialog(string title, string message, string okText, bool danger = true) : base(title, 400, 210)
    {
        var box = new StackPanel();

        box.Children.Add(new TextBlock
        {
            Text = message,
            FontFamily = Theme.Ui,
            FontSize = 12.5,
            LineHeight = 19,
            Foreground = Theme.Dim,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };

        var cancel = Kit.Button("取消", false, () => { DialogResult = false; Close(); });
        cancel.Margin = new Thickness(0, 0, 8, 0);
        bar.Children.Add(cancel);

        if (danger)
        {
            var dangerBtn = Kit.Button(okText, false, () => { DialogResult = true; Close(); });
            if (dangerBtn.Child is TextBlock tb) tb.Foreground = Theme.Danger;
            bar.Children.Add(dangerBtn);
        }
        else
        {
            bar.Children.Add(Kit.Button(okText, true, () => { DialogResult = true; Close(); }));
        }

        box.Children.Add(bar);
        Body.Children.Add(box);
    }
}
