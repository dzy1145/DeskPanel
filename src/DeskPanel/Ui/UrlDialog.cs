using System.Windows;
using System.Windows.Controls;

namespace DeskPanel.Ui;

/// <summary>添加网址：名称 + 地址，图标之后自动抓 favicon。</summary>
internal sealed class UrlDialog : DialogShell
{
    public AppItem Result { get; private set; }

    private readonly TextBox _name = Kit.Input();
    private readonly TextBox _url = Kit.Input();

    public UrlDialog() : base("添加网址", 440, 300)
    {
        var box = new StackPanel();
        box.Children.Add(Kit.Label("名称（留空自动取域名）"));
        box.Children.Add(Kit.Field(_name));
        box.Children.Add(new Border { Height = 12 });
        box.Children.Add(Kit.Label("网址"));
        box.Children.Add(Kit.Field(_url));

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0),
        };
        var cancel = Kit.Button("取消", false, Close);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        bar.Children.Add(cancel);
        bar.Children.Add(Kit.Button("添加", true, Accept));
        box.Children.Add(bar);

        Body.Children.Add(box);
        Loaded += (_, _) => _url.Focus();
    }

    private void Accept()
    {
        string raw = _url.Text.Trim();
        if (raw.Length == 0) return;

        if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            raw = "https://" + raw;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return;

        string name = _name.Text.Trim();
        if (name.Length == 0)
        {
            name = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? uri.Host[4..]
                : uri.Host;
        }

        Result = new AppItem { Name = name, Target = uri.ToString() };
        DialogResult = true;
        Close();
    }
}
