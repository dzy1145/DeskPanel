using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeskPanel.Ui;

/// <summary>
/// 常用区网格：自己算行列，重排时用弹簧积分把每个格子「弹」到新位置（安卓桌面那种避让）。
/// </summary>
internal sealed class TileBoard : Canvas
{
    public int Columns { get; set; } = 3;
    public double CellHeight { get; set; } = 60;
    public double CellGap { get; } = 4;

    /// <summary>正在被拖动的格子：不参与弹簧位移（跟手位移由 AppPanel 控制）。</summary>
    public UIElement SkipAnimation { get; set; }

    private double _cellWidth;
    private readonly Dictionary<UIElement, Point> _targets = new();
    private readonly Dictionary<UIElement, Vector> _velocity = new();
    private bool _running;
    private double _lastTime;

    // 果冻感：欠阻尼弹簧
    private const double Omega = 22.0;
    private const double Zeta = 0.52;

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0 ? 300 : availableSize.Width;
        _cellWidth = w / Math.Max(1, Columns);

        int rows = (Children.Count + Columns - 1) / Columns;
        var size = new Size(w, Math.Max(CellHeight, rows * CellHeight));

        foreach (UIElement child in Children)
            child.Measure(new Size(_cellWidth - CellGap, CellHeight - CellGap));

        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _cellWidth = finalSize.Width / Math.Max(1, Columns);

        foreach (UIElement child in Children)
        {
            double x = GetLeft(child);
            double y = GetTop(child);
            if (double.IsNaN(x)) x = 0;
            if (double.IsNaN(y)) y = 0;

            child.Arrange(new Rect(x, y, _cellWidth - CellGap, CellHeight - CellGap));
        }
        return finalSize;
    }

    /// <summary>按当前 Children 顺序重排；animate=true 时用弹簧位移过去。</summary>
    public void Relayout(bool animate)
    {
        if (ActualWidth > 0) _cellWidth = ActualWidth / Math.Max(1, Columns);
        if (_cellWidth <= 0) return;

        int i = 0;
        foreach (UIElement child in Children)
        {
            var target = new Point((i % Columns) * _cellWidth, (i / Columns) * CellHeight);
            _targets[child] = target;

            if (!animate)
            {
                SetLeft(child, target.X);
                SetTop(child, target.Y);
                _velocity[child] = default;
            }
            else if (!_velocity.ContainsKey(child))
            {
                _velocity[child] = default;
            }
            i++;
        }

        if (animate) Start();
        else
        {
            Stop();
            InvalidateMeasure();
        }
    }

    /// <summary>把某个格子从「跟手位置」交还给弹簧（拖动松手时用）。</summary>
    public void SeedFromCurrent(UIElement child)
    {
        _targets[child] = new Point(GetLeft(child), GetTop(child));
        _velocity[child] = default;
    }

    private void Start()
    {
        if (_running) return;
        _running = true;
        _lastTime = 0;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object sender, EventArgs e)
    {
        double now = e is RenderingEventArgs rea ? rea.RenderingTime.TotalSeconds : _lastTime + 1.0 / 60;
        double dt = _lastTime <= 0 ? 1.0 / 60 : now - _lastTime;
        _lastTime = now;
        dt = Math.Clamp(dt, 0.001, 0.05);

        bool busy = false;

        foreach (UIElement child in Children)
        {
            if (ReferenceEquals(child, SkipAnimation)) continue;
            if (!_targets.TryGetValue(child, out var target)) continue;

            double x = GetLeft(child);
            double y = GetTop(child);
            if (double.IsNaN(x) || double.IsNaN(y)) continue;

            _velocity.TryGetValue(child, out var v);

            double ax = Omega * Omega * (target.X - x) - 2 * Zeta * Omega * v.X;
            double ay = Omega * Omega * (target.Y - y) - 2 * Zeta * Omega * v.Y;

            v = new Vector(v.X + ax * dt, v.Y + ay * dt);
            x += v.X * dt;
            y += v.Y * dt;

            bool settled = Math.Abs(target.X - x) < 0.25 && Math.Abs(target.Y - y) < 0.25 &&
                           Math.Abs(v.X) < 2 && Math.Abs(v.Y) < 2;

            if (settled)
            {
                x = target.X;
                y = target.Y;
                v = default;
            }
            else
            {
                busy = true;
            }

            _velocity[child] = v;
            SetLeft(child, x);
            SetTop(child, y);
        }

        if (!busy) Stop();
    }

    /// <summary>光标落在第几个格子（含末尾追加位）。</summary>
    public int IndexAt(Point p)
    {
        if (_cellWidth <= 0) return 0;
        int col = (int)Math.Floor(p.X / _cellWidth);
        int row = (int)Math.Floor(p.Y / CellHeight);
        col = Math.Clamp(col, 0, Columns - 1);
        row = Math.Max(0, row);
        int index = row * Columns + col;
        return Math.Clamp(index, 0, Math.Max(0, Children.Count - 1));
    }

    public void MoveChild(int from, int to)
    {
        if (from < 0 || to < 0 || from >= Children.Count || to >= Children.Count || from == to) return;

        var child = Children[from];
        Children.RemoveAt(from);
        Children.Insert(to, child);
        InvalidateMeasure();
    }
}
