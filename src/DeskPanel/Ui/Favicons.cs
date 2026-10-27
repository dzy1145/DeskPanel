using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskPanel.Ui;

/// <summary>网址图标：本地缓存 + 三级抓取（站点 favicon → 页面 link rel=icon → 公共服务）。</summary>
internal static class Favicons
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        return c;
    }

    public static string CacheDir
    {
        get
        {
            string dir = Path.Combine(Config.Dir, "icons");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string CachePath(string url) => Path.Combine(CacheDir, "url-" + Hash(url) + ".img");

    private static string Hash(string text)
    {
        using var sha = SHA1.Create();
        byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToLowerInvariant()));
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }

    public static ImageSource LoadCached(string url)
    {
        try
        {
            string path = CachePath(url);
            if (!File.Exists(path)) return null;
            return Decode(File.ReadAllBytes(path));
        }
        catch { return null; }
    }

    public static Task<ImageSource> FetchAsync(string url) => Task.Run(() => Fetch(url));

    private static ImageSource Fetch(string url)
    {
        try
        {
            var cached = LoadCached(url);
            if (cached != null) return cached;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            string host = uri.Host;

            var bytes = Get($"https://{host}/favicon.ico")
                        ?? FromHtml(uri)
                        ?? Get($"https://favicon.im/{host}")
                        ?? Get($"https://api.iowen.cn/favicon/{host}.png");

            if (bytes == null) return null;

            var image = Decode(bytes);
            if (image == null) return null;

            File.WriteAllBytes(CachePath(url), bytes);
            return image;
        }
        catch { return null; }
    }

    private static byte[] FromHtml(Uri page)
    {
        try
        {
            string html = Http.GetStringAsync(page).GetAwaiter().GetResult();
            if (html.Length > 400_000) html = html[..400_000];

            var matches = Regex.Matches(html,
                "<link[^>]+(?:rel\\s*=\\s*[\"'](?:shortcut )?icon[\"'][^>]*href\\s*=\\s*[\"']([^\"']+)[\"']|href\\s*=\\s*[\"']([^\"']+)[\"'][^>]*rel\\s*=\\s*[\"'](?:shortcut )?icon[\"'])",
                RegexOptions.IgnoreCase);

            foreach (Match m in matches)
            {
                string href = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                if (string.IsNullOrWhiteSpace(href)) continue;
                if (!Uri.TryCreate(page, href, out var iconUri)) continue;
                var data = Get(iconUri.ToString());
                if (data != null && Decode(data) != null) return data;
            }
        }
        catch { }
        return null;
    }

    private static byte[] Get(string url)
    {
        try
        {
            using var resp = Http.GetAsync(url).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) return null;
            var bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            return bytes.Length is > 0 and < 512 * 1024 ? bytes : null;
        }
        catch { return null; }
    }

    private static ImageSource Decode(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch { return null; }
    }

    // ---- 字母块兜底（青柠那种纯色块 + 首字）----

    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x2B, 0xE0, 0xC8),
        Color.FromRgb(0x7C, 0x8C, 0xFF),
        Color.FromRgb(0xFF, 0x9F, 0x43),
        Color.FromRgb(0xFF, 0x6B, 0x9D),
        Color.FromRgb(0x4D, 0xD0, 0x8F),
        Color.FromRgb(0xB0, 0x8C, 0xFF),
    };

    public static (Brush Background, string Text) Letter(string name, string seed)
    {
        int hash = 0;
        foreach (char c in seed ?? "") hash = (hash * 31 + c) & 0x7FFFFFFF;

        Color color = Palette[hash % Palette.Length];
        var brush = new SolidColorBrush(Color.FromArgb(0xE6, color.R, color.G, color.B));
        brush.Freeze();

        string text = string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpper(CultureInfo.CurrentCulture);
        return (brush, text);
    }
}
