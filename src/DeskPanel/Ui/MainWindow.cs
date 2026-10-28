using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DeskPanel.Interop;
using DeskPanel.Metrics;
using DeskPanel.Services;
using DeskPanel.Startup;
using Microsoft.Win32;

namespace DeskPanel.Ui;

public sealed class MainWindow : Window
{
	private readonly Config _cfg;

	private readonly Dictionary<string, MetricRow> _metricRows = new Dictionary<string, MetricRow>();

	/// <summary>指标区的容器（盘符行热替换要用）。</summary>
	private StackPanel _metricHost;

	private readonly StackPanel _startupList = new StackPanel();

	private ScrollViewer _startupScroll;



	private FrameworkElement _rootUi;

	private readonly TextBlock _clock = new TextBlock();

	private readonly TextBlock _hint = new TextBlock();

	private Ellipse _servicesDot;

	private readonly List<ServiceRow> _serviceRows = new();

	private StackPanel _servicesSection;


	private DesktopLayer _layer;

	private MetricsEngine _metrics;

	private Tray _tray;

	private AppPanel _appPanel;

	private FileDropTarget _dropTarget;

	private DispatcherTimer _clockTimer;

	private DispatcherTimer _botTimer;

	private DispatcherTimer _startupTimer;

	private DispatcherTimer _usageTimer;

	private UsageRing _ring5h;

	private UsageRing _ringWeek;

	private UsageRing _ringMonth;

	private TextBlock _usageHint;

	private bool _usageBusy;

	private Border _usageTitle;

	private TextBlock _usageTitleText;

	private Grid _usageBody;

	private StackPanel _usageSection;

	private UniformGrid _goView;

	private FrameworkElement _dsView;

	private TextBlock _dsTotal;

	private TextBlock _dsSub;

	private string _mode = "go";

	private bool _balanceBusy;

	private DeepSeekBalance.Snapshot _lastBalance;

	private OpenCodeUsage.Snapshot _lastUsage;

	public MainWindow()
	{
		_cfg = Config.Load();
		base.Title = "DeskPanel";
		base.WindowStyle = WindowStyle.None;
		base.ResizeMode = ResizeMode.NoResize;
		base.ShowInTaskbar = false;
		base.AllowsTransparency = !_cfg.Acrylic;
		base.Background = Brushes.Transparent;
		base.Width = _cfg.Width;
		base.Height = 900.0;
		base.WindowStartupLocation = WindowStartupLocation.Manual;
		_rootUi = (FrameworkElement)BuildUi();
		base.Content = _rootUi;
		base.Loaded += OnLoaded;
		base.Closed += OnClosed;
	}

	private UIElement BuildUi()
	{
		Grid grid = new Grid();
		Border element = new Border
		{
			Background = ((_cfg.Theme == "solid") ? Theme.SurfaceSolid : Theme.Surface),
			BorderBrush = Theme.Hair,
			BorderThickness = new Thickness(1.0, 0.0, 0.0, 0.0)
		};
		grid.Children.Add(element);
		Grid grid2 = new Grid
		{
			Margin = new Thickness(15.0, 16.0, 14.0, 12.0)
		};
		for (int i = 0; i < 9; i++)
		{
			grid2.RowDefinitions.Add(new RowDefinition
			{
				Height = GridLength.Auto
			});
		}
		grid2.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(1.0, GridUnitType.Star)
		});
		grid2.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		Grid grid3 = new Grid();
		grid3.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid3.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid3.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		_clock.FontFamily = Theme.Num;
		_clock.FontSize = 17.0;
		_clock.Foreground = Theme.Text;
		_clock.Text = DateTime.Now.ToString(_cfg.ClockSeconds > 0 ? "HH:mm:ss" : "HH:mm");
		_clock.Visibility = (_cfg.ShowClock ? Visibility.Visible : Visibility.Collapsed);
		grid3.Children.Add(_clock);
		_hint.FontFamily = Theme.Ui;
		_hint.FontSize = 9.5;
		_hint.Foreground = Theme.Faint;
		_hint.VerticalAlignment = VerticalAlignment.Bottom;
		_hint.Margin = new Thickness(0.0, 0.0, 0.0, 2.0);
		_hint.Text = "Win+D";
		Grid.SetColumn(_hint, 2);
		grid3.Children.Add(_hint);

		// 右上角齿轮：打开可视化设置
		var gear = new Border
		{
			Width = 20,
			Height = 20,
			Margin = new Thickness(0, 0, 6, 0),
			CornerRadius = new CornerRadius(6),
			Background = Brushes.Transparent,
			VerticalAlignment = VerticalAlignment.Center,
			Cursor = Cursors.Hand,
			ToolTip = "设置",
			Child = new TextBlock
			{
				Text = "\u2699",
				FontFamily = Theme.Ui,
				FontSize = 11.5,
				Foreground = Theme.Faint,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			}
		};
		gear.MouseEnter += delegate { gear.Background = Theme.Hover; };
		gear.MouseLeave += delegate { gear.Background = Brushes.Transparent; };
		gear.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
		{
			e.Handled = true;
			OpenSettings();
		};
		Grid.SetColumn(gear, 1);
		grid3.Children.Add(gear);
		grid2.Children.Add(grid3);
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(0.0, 16.0, 0.0, 0.0)
		};
		_metricHost = stackPanel;
		AddMetric(stackPanel, "CPU", "cpu", Theme.Accent);
		AddMetric(stackPanel, "GPU", "gpu", Theme.Purple);
		AddMetric(stackPanel, "内存", "mem", Theme.Text);
		foreach (string drife in _cfg.Drives)
		{
			AddMetric(stackPanel, drife, "drive:" + drife, Theme.Frozen(byte.MaxValue, 184, 196, 214));
		}
		Grid.SetRow(stackPanel, 1);
		grid2.Children.Add(stackPanel);
		Border element2 = Parts.Divider();
		Grid.SetRow(element2, 2);
		grid2.Children.Add(element2);
		AppPanel element3 = (_appPanel = new AppPanel(_cfg, delegate(string msg)
		{
			_hint.Text = msg;
		}));
		Grid.SetRow(element3, 3);
		grid2.Children.Add(element3);
		Border element4 = Parts.Divider();
		Grid.SetRow(element4, 4);
		grid2.Children.Add(element4);
		StackPanel stackPanel2 = new StackPanel();
		Grid grid4 = new Grid();
		grid4.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid4.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		StackPanel stackPanel3 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center
		};
		_servicesDot = MakeDot();
		_servicesDot.Margin = new Thickness(0.0, 0.0, 5.0, 0.0);
		stackPanel3.Children.Add(_servicesDot);
		stackPanel3.Children.Add(new TextBlock
		{
			Text = "服务",
			FontFamily = Theme.Ui,
			FontSize = 10.0,
			Foreground = Theme.Faint,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(4.0, 0.0, 0.0, 0.0)
		});
		grid4.Children.Add(stackPanel3);
		// 服务标题右侧的「+ 新增」
		var addService = new Pill("+ 新增", Theme.Dim, delegate
		{
			AddServiceClicked();
		});
		addService.HorizontalAlignment = HorizontalAlignment.Right;
		addService.VerticalAlignment = VerticalAlignment.Center;
		Grid.SetColumn(addService, 1);
		grid4.Children.Add(addService);
		stackPanel2.Children.Add(grid4);

		_servicesSection = new StackPanel
		{
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0)
		};
		stackPanel2.Children.Add(_servicesSection);
		RebuildServiceRows();
		stackPanel2.Visibility = Visibility.Visible;
		Grid.SetRow(stackPanel2, 5);
		grid2.Children.Add(stackPanel2);
		Border element5 = Parts.Divider();
		Grid.SetRow(element5, 6);
		grid2.Children.Add(element5);
		Grid grid5 = new Grid();
		grid5.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid5.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(1.0, GridUnitType.Star)
		});
		Grid grid6 = new Grid();
		grid6.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid6.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid6.Children.Add(Parts.Section("开机启动"));
		Pill addStartup = null;
		addStartup = new Pill("+ 新增", Theme.Dim, delegate
		{
			ShowStartupAddMenu(addStartup);
		});
		addStartup.VerticalAlignment = VerticalAlignment.Top;
		Grid.SetColumn(addStartup, 1);
		grid6.Children.Add(addStartup);
		grid5.Children.Add(grid6);
		ScrollViewer scrollViewer = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			Content = _startupList,
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
			MaxHeight = 196.0
		};
		Grid.SetRow(scrollViewer, 1);
		_startupScroll = scrollViewer;
		grid5.Children.Add(scrollViewer);

		Grid.SetRow(grid5, 7);
		grid2.Children.Add(grid5);
		StackPanel stackPanel5 = new StackPanel
		{
			Margin = new Thickness(0.0, 14.0, 0.0, 0.0)
		};
		Grid grid7 = new Grid();
		grid7.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid7.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		_usageTitle = new Border
		{
			CornerRadius = new CornerRadius(6.0),
			Background = Brushes.Transparent,
			Padding = new Thickness(4.0, 2.0, 6.0, 2.0),
			HorizontalAlignment = HorizontalAlignment.Stretch,
			Cursor = Cursors.Hand,
			ToolTip = "点一下切换：OpenCode Go 三环 ⇄ DeepSeek 官方余额"
		};
		TextBlock titleText = Parts.Section("额度");
		titleText.Margin = new Thickness(0.0);
		titleText.VerticalAlignment = VerticalAlignment.Center;
		_usageTitleText = titleText;
		_usageTitle.Child = titleText;
		_usageTitle.MouseEnter += delegate
		{
			titleText.Foreground = Theme.Text;
			_usageTitle.Background = Theme.Hover;
		};
		_usageTitle.MouseLeave += delegate
		{
			titleText.Foreground = Theme.Faint;
			_usageTitle.Background = Brushes.Transparent;
		};
		_usageTitle.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
		{
			e.Handled = true;
			ToggleUsageMode();
		};
		grid7.Children.Add(_usageTitle);
		_usageHint = new TextBlock
		{
			Text = "",
			FontFamily = Theme.Ui,
			FontSize = 10.0,
			Foreground = Theme.Faint,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid.SetColumn(_usageHint, 1);
		grid7.Children.Add(_usageHint);
		stackPanel5.Children.Add(grid7);
		_usageBody = new Grid
		{
			Height = 88.0,
			Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
		};
		_goView = new UniformGrid
		{
			Columns = 3,
			Cursor = Cursors.Hand,
			Background = Brushes.Transparent,
			ToolTip = "OpenCode Go 订阅额度，点一下重新拉取"
		};
		_ring5h = new UsageRing
		{
			Label = "5h",
			Window = TimeSpan.FromHours(5.0)
		};
		_ringWeek = new UsageRing
		{
			Label = "周",
			Window = TimeSpan.FromDays(7.0)
		};
		_ringMonth = new UsageRing
		{
			Label = "月",
			Window = TimeSpan.FromDays(30.0)
		};
		_goView.Children.Add(_ring5h);
		_goView.Children.Add(_ringWeek);
		_goView.Children.Add(_ringMonth);
		_goView.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
		{
			e.Handled = true;
			RefreshUsage();
		};
		_usageBody.Children.Add(_goView);
		_dsView = BuildDeepSeekView();
		_usageBody.Children.Add(_dsView);
		stackPanel5.Children.Add(_usageBody);
		Grid.SetRow(stackPanel5, 10);
		grid2.Children.Add(stackPanel5);
		_usageSection = stackPanel5;
		// 没启用额度区就整块隐藏，也不发请求
		stackPanel5.Visibility = (_cfg.Usage.Enabled ? Visibility.Visible : Visibility.Collapsed);
		OpenCodeUsage.Snapshot snapshot = OpenCodeUsage.LoadCache();
		if (snapshot != null)
		{
			ApplyUsage(snapshot, stale: true);
		}
		DeepSeekBalance.Snapshot snapshot2 = DeepSeekBalance.LoadCache();
		if (snapshot2 != null)
		{
			ApplyBalance(snapshot2, stale: true);
		}
		_mode = (string.IsNullOrWhiteSpace(_cfg.Usage.Mode) ? "go" : _cfg.Usage.Mode.Trim().ToLowerInvariant());
		ShowUsageBody(animated: false);
		RefreshUsage();
		RefreshBalance();
		_usageTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMinutes(1.0)
		};
		_usageTimer.Tick += delegate
		{
			RefreshUsage();
			RefreshBalance();
		};
		if (_cfg.Usage.Enabled)
		{
			_usageTimer.Start();
		}
		grid.Children.Add(grid2);
		return grid;
	}

	private FrameworkElement BuildDeepSeekView()
	{
		StackPanel obj = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Center,
			Cursor = Cursors.Hand,
			ToolTip = "DeepSeek 官方余额，点一下重新拉取",
			Visibility = Visibility.Collapsed
		};
		_dsTotal = new TextBlock
		{
			Text = "¥--",
			FontFamily = Theme.Num,
			FontSize = 30.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = Theme.Text,
			HorizontalAlignment = HorizontalAlignment.Center
		};
		obj.Children.Add(_dsTotal);
		_dsSub = new TextBlock
		{
			Text = "",
			FontFamily = Theme.Ui,
			FontSize = 10.5,
			Foreground = Theme.Faint,
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
		};
		obj.Children.Add(_dsSub);
		obj.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
		{
			e.Handled = true;
			RefreshBalance();
		};
		return obj;
	}

	private void ToggleUsageMode()
	{
		_mode = ((_mode == "deepseek") ? "go" : "deepseek");
		_cfg.Usage.Mode = _mode;
		_cfg.Save();
		ShowUsageBody(animated: true);
		if (_mode == "deepseek")
		{
			RefreshBalance();
		}
		else
		{
			RefreshUsage();
		}
	}

	private void ShowUsageBody(bool animated)
	{
		bool flag = _mode == "deepseek";
		FrameworkElement frameworkElement = (flag ? _dsView : _goView);
		FrameworkElement hide = (flag ? _goView : _dsView);
		_usageTitleText.Text = (flag ? "额度 · DeepSeek ⇄" : "额度 · Go ⇄");
		frameworkElement.Visibility = Visibility.Visible;
		frameworkElement.IsHitTestVisible = true;
		if (!animated)
		{
			hide.Visibility = Visibility.Collapsed;
			hide.Opacity = 1.0;
			frameworkElement.Opacity = 1.0;
			frameworkElement.RenderTransform = Transform.Identity;
			return;
		}
		IEasingFunction ease = Theme.Ease;
		frameworkElement.Opacity = 0.0;
		TranslateTransform translateTransform = (TranslateTransform)(frameworkElement.RenderTransform = new TranslateTransform(0.0, 10.0));
		hide.IsHitTestVisible = false;
		hide.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(110.0))
		{
			EasingFunction = ease
		});
		translateTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(260.0))
		{
			EasingFunction = ease
		});
		frameworkElement.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(210.0))
		{
			EasingFunction = ease,
			BeginTime = TimeSpan.FromMilliseconds(70.0)
		});
		DispatcherTimer timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(130.0)
		};
		timer.Tick += delegate
		{
			timer.Stop();
			hide.Visibility = Visibility.Collapsed;
			hide.BeginAnimation(UIElement.OpacityProperty, null);
			hide.Opacity = 1.0;
		};
		timer.Start();
	}

	private void RefreshBalance()
	{
		if (!_cfg.Usage.Enabled || _balanceBusy)
		{
			return;
		}
		_balanceBusy = true;
		string key = _cfg.Usage.DeepSeekKey;
		Task.Run(() => DeepSeekBalance.FetchAsync(key)).ContinueWith(delegate(Task<DeepSeekBalance.Snapshot> t)
		{
			if (t.Status == TaskStatus.RanToCompletion)
			{
				DeepSeekBalance.Snapshot snap = t.Result;
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					_balanceBusy = false;
					if (snap.Error != null)
					{
						DeepSeekBalance.Snapshot snapshot = _lastBalance ?? DeepSeekBalance.LoadCache();
						if (snapshot != null)
						{
							ApplyBalance(snapshot, stale: true);
							if (_mode == "deepseek")
							{
								_usageHint.Text = snapshot.FetchedAt.ToString("HH:mm") + " · 缓存";
								_usageHint.ToolTip = "这一轮没取到（" + snap.Error + "），显示上次成功的余额";
							}
						}
						else if (_mode == "deepseek")
						{
							_usageHint.Text = "取余额失败";
							_usageHint.ToolTip = snap.Error;
						}
						Log.Write("balance: " + snap.Error);
					}
					else
					{
						ApplyBalance(snap, stale: false);
						DeepSeekBalance.SaveCache(snap);
						Log.Write($"balance: {snap.Total:0.00} {snap.Currency}");
					}
				});
			}
		});
	}

	private void ApplyBalance(DeepSeekBalance.Snapshot snap, bool stale)
	{
		_lastBalance = snap;
		string value = (string.Equals(snap.Currency, "CNY", StringComparison.OrdinalIgnoreCase) ? "¥" : "$");
		_dsTotal.Text = $"{value}{snap.Total:0.00}";
		_dsTotal.Foreground = ((snap.Total <= 5.0) ? Theme.Warn : Theme.Text);
		_dsSub.Text = $"赠金 {value}{snap.Granted:0.00} · 充值 {value}{snap.ToppedUp:0.00}";
		if (!stale && _mode == "deepseek")
		{
			_usageHint.Text = snap.FetchedAt.ToString("HH:mm");
			_usageHint.ToolTip = "DeepSeek 官方余额，点数字重新拉取";
		}
	}

	private void RefreshUsage()
	{
		if (!_cfg.Usage.Enabled || _usageBusy)
		{
			return;
		}
		_usageBusy = true;
		string key = _cfg.Usage.OpenCodeKey;
		Task.Run(() => OpenCodeUsage.FetchAsync(key)).ContinueWith(delegate(Task<OpenCodeUsage.Snapshot> t)
		{
			if (t.Status == TaskStatus.RanToCompletion)
			{
				OpenCodeUsage.Snapshot snap = t.Result;
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					_usageBusy = false;
					if (snap.Error != null)
					{
						OpenCodeUsage.Snapshot snapshot = _lastUsage ?? OpenCodeUsage.LoadCache();
						if (snapshot != null)
						{
							ApplyUsage(snapshot, stale: true);
							_usageHint.Text = snapshot.FetchedAt.ToString("HH:mm") + " · 缓存";
							_usageHint.ToolTip = "这一轮没取到（" + snap.Error + "），显示的是上次成功的数据；点环重试";
						}
						else
						{
							_ring5h.Set(0, default(DateTime), hasData: false);
							_ringWeek.Set(0, default(DateTime), hasData: false);
							_ringMonth.Set(0, default(DateTime), hasData: false);
							_usageHint.Text = "取额度失败（点环重试）";
							_usageHint.ToolTip = snap.Error;
						}
						Log.Write("usage: " + snap.Error);
					}
					else
					{
						ApplyUsage(snap, stale: false);
						OpenCodeUsage.SaveCache(snap);
						Log.Write($"usage: 5h={snap.Rolling.Percent}% 周={snap.Weekly.Percent}% 月={snap.Monthly.Percent}%");
					}
				});
			}
		});
	}

	private void ApplyUsage(OpenCodeUsage.Snapshot snap, bool stale)
	{
		_lastUsage = snap;
		_ring5h.Set(snap.Rolling.Percent, snap.Rolling.ResetsAt, hasData: true, snap.Rolling.Ok);
		_ringWeek.Set(snap.Weekly.Percent, snap.Weekly.ResetsAt, hasData: true, snap.Weekly.Ok);
		_ringMonth.Set(snap.Monthly.Percent, snap.Monthly.ResetsAt, hasData: true, snap.Monthly.Ok);
		if (!stale)
		{
			_usageHint.Text = snap.FetchedAt.ToString("HH:mm");
			_usageHint.ToolTip = "OpenCode Go 订阅额度，点环重新拉取";
		}
	}

	/// <summary>按当前配置重建盘符行（盘符热更改用）。</summary>
	private void RebuildDriveRows()
	{
		if (_metricHost == null)
		{
			return;
		}

		foreach (string key in _metricRows.Keys.Where(k => k.StartsWith("drive:", StringComparison.OrdinalIgnoreCase)).ToList())
		{
			if (_metricRows.TryGetValue(key, out MetricRow row))
			{
				_metricHost.Children.Remove(row);
				_metricRows.Remove(key);
			}
		}

		foreach (string drive in _cfg.Drives)
		{
			AddMetric(_metricHost, drive, "drive:" + drive, Theme.Frozen(byte.MaxValue, 184, 196, 214));
		}
	}
	private void AddMetric(Panel host, string label, string key, Brush accent)
	{
		MetricRow metricRow = new MetricRow(label, accent);
		_metricRows[key] = metricRow;
		host.Children.Add(metricRow);
	}

	private static Ellipse MakeDot()
	{
		return new Ellipse
		{
			Width = 6.0,
			Height = 6.0,
			Fill = Theme.DotOff,
			VerticalAlignment = VerticalAlignment.Center
		};
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		nint handle = new WindowInteropHelper(this).Handle;
		HwndSource.FromHwnd(handle)?.AddHook(WndProc);
		_layer = new DesktopLayer(this, _cfg.DesktopLayer, _cfg.NoActivate);
		_layer.Attach();
		ApplyResponsiveLayout();
		SystemParameters.StaticPropertyChanged += OnSystemParamsChanged;
		Native.DragAcceptFiles(handle, accept: true);
		_dropTarget = new FileDropTarget(delegate(string[] paths, Point screenPx)
		{
			_appPanel?.AcceptFiles(paths, screenPx);
		}, delegate(bool hovering)
		{
			if (hovering)
			{
				_hint.Text = "松手放到常用";
			}
		});
		OleDrop.Install(handle, _dropTarget);
		if (_cfg.Acrylic)
		{
			Acrylic.TryApply(handle, -871297780);
		}
		_tray = new Tray(handle, this);
		base.AllowDrop = true;
		base.DragEnter += delegate(object _, DragEventArgs e2)
		{
			Log.Write($"drop: 窗口 DragEnter，FileDrop={e2.Data.GetDataPresent(DataFormats.FileDrop)}");
		};
		base.DragOver += delegate(object _, DragEventArgs e2)
		{
			if (e2.Data.GetDataPresent(DataFormats.FileDrop))
			{
				e2.Effects = DragDropEffects.Copy;
				e2.Handled = true;
			}
		};
		base.Drop += delegate(object _, DragEventArgs e2)
		{
			if (e2.Data.GetData(DataFormats.FileDrop) is string[] array && array.Length != 0)
			{
				_appPanel?.AcceptFiles(array, PointToScreen(e2.GetPosition(this)));
			}
			e2.Handled = true;
		};
		_metrics = new MetricsEngine(_cfg.RefreshMs, _cfg.Drives);
		_metrics.Sampled += OnSample;
		_clockTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1.0)
		};
		_clockTimer.Tick += delegate
		{
			_clock.Text = DateTime.Now.ToString(_cfg.ClockSeconds > 0 ? "HH:mm:ss" : "HH:mm");
		_clock.Visibility = (_cfg.ShowClock ? Visibility.Visible : Visibility.Collapsed);
		};
		_clockTimer.Start();
		ReloadStartup();
		CheckWriteAccess();
		_startupTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(20.0)
		};
		_startupTimer.Tick += delegate
		{
			ReloadStartup();
		};
		_startupTimer.Start();
		_botTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(5.0)
		};
		_botTimer.Tick += delegate
		{
			ScheduleProbe(0);
		};
		_botTimer.Start();
		ScheduleProbe(300);
	}

	internal void ApplyResponsiveLayout()
	{
		double height = SystemParameters.WorkArea.Height;
		int rows = ((height >= 1150.0) ? 8 : ((height >= 880.0) ? 7 : ((height >= 800.0) ? 6 : ((height >= 720.0) ? 5 : 4))));
		if (_startupScroll != null)
		{
			_startupScroll.MaxHeight = rows * 28;
		}
		Log.Write($"layout: 工作区高 {height:0} DIP → 开机启动 {rows} 行");
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			FitToHeight(rows);
		}, DispatcherPriority.Loaded);
	}

	private void FitToHeight(int rows)
	{
		try
		{
			if (_rootUi == null || base.ActualHeight <= 0.0)
			{
				return;
			}
			UpdateLayout();
			int num = 0;
			while (_rootUi.DesiredSize.Height > base.ActualHeight - 10.0 && rows > 3 && num++ < 8)
			{
				rows--;
				if (_startupScroll != null)
				{
					_startupScroll.MaxHeight = rows * 28;
				}
				UpdateLayout();
			}
			double height = _rootUi.DesiredSize.Height;
			double num2 = Math.Max(100.0, base.ActualHeight - 10.0);
			double num3 = ((height > num2 && height > 0.0) ? Math.Max(0.75, num2 / height) : 1.0);
			if (!(_rootUi.LayoutTransform is ScaleTransform scaleTransform) || !(Math.Abs(scaleTransform.ScaleY - num3) < 0.01))
			{
				if (num3 >= 0.999)
				{
					_rootUi.LayoutTransform = Transform.Identity;
					Log.Write($"layout: 内容 {height:0} DIP / 可用 {base.ActualHeight:0} DIP，无需缩放");
				}
				else
				{
					ScaleTransform layoutTransform = new ScaleTransform(num3, num3);
					_rootUi.LayoutTransform = layoutTransform;
					Log.Write($"layout: 内容 {height:0} DIP > 可用 {base.ActualHeight:0} DIP，整体缩到 {num3:P0}");
				}
			}
		}
		catch (Exception ex)
		{
			Log.Ex(ex);
		}
	}

	private void CheckWriteAccess()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
			if (registryKey == null)
			{
				throw new UnauthorizedAccessException("Run 键打不开可写");
			}
		}
		catch (Exception ex)
		{
			Log.Write("自检: 注册表不可写 :: " + ex.Message);
			_hint.Text = "权限受限：托盘退出后双击桌面快捷方式重启面板";
		}
	}

	private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		switch (msg)
		{
		case 5121:
			_tray?.OnMessage(((IntPtr)lParam).ToInt32());
			handled = true;
			break;
		case 126:
		case 736:
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				try
				{
					_layer?.Place();
					ApplyResponsiveLayout();
				}
				catch (Exception ex)
				{
					Log.Ex(ex);
				}
			});
			break;
		case 563:
			HandleDropFiles(wParam);
			handled = true;
			break;
		}
		return IntPtr.Zero;
	}

	private void HandleDropFiles(nint hDrop)
	{
		try
		{
			uint num = Native.DragQueryFileW(hDrop, uint.MaxValue, null, 0u);
			List<string> list = new List<string>();
			for (uint num2 = 0u; num2 < num; num2++)
			{
				StringBuilder stringBuilder = new StringBuilder(1024);
				if (Native.DragQueryFileW(hDrop, num2, stringBuilder, (uint)stringBuilder.Capacity) != 0)
				{
					list.Add(stringBuilder.ToString());
				}
			}
			double num3 = VisualTreeHelper.GetDpi(this).DpiScaleX;
			if (num3 <= 0.0)
			{
				num3 = 1.0;
			}
			Point screenPoint = new Point(0.0, 0.0);
			if (Native.DragQueryPoint(hDrop, out var point))
			{
				screenPoint = PointToScreen(new Point((double)point.X / num3, (double)point.Y / num3));
			}
			Native.DragFinish(hDrop);
			Log.Write($"drop: WM_DROPFILES 收到 {list.Count} 项");
			if (list.Count > 0)
			{
				_appPanel?.AcceptFiles(list.ToArray(), screenPoint);
			}
		}
		catch (Exception ex)
		{
			Log.Ex(ex);
		}
	}

	private void OnSystemParamsChanged(object sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName != "WorkArea")
		{
			return;
		}
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			try
			{
				_layer?.Place();
				ApplyResponsiveLayout();
			}
			catch (Exception ex)
			{
				Log.Ex(ex);
			}
		});
	}

	private void OnClosed(object sender, EventArgs e)
	{
		SystemParameters.StaticPropertyChanged -= OnSystemParamsChanged;
		_clockTimer?.Stop();
		_botTimer?.Stop();
		_startupTimer?.Stop();
		_usageTimer?.Stop();
		_metrics?.Dispose();
		_layer?.Dispose();
		_tray?.Dispose();
		Application.Current?.Shutdown();
	}

	private void OnSample(Sample s)
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			if (_metricRows.TryGetValue("cpu", out var value))
			{
				value.Set(s.Cpu, (s.Cpu >= 85.0) ? Theme.Warn : Theme.Text);
			}
			if (_metricRows.TryGetValue("gpu", out var value2))
			{
				value2.Set(s.Gpu, (s.Gpu >= 85.0) ? Theme.Warn : Theme.Text);
			}
			if (_metricRows.TryGetValue("mem", out var value3))
			{
				value3.Set(s.MemPercent, (s.MemPercent >= 88.0) ? Theme.Warn : Theme.Text);
			}
			UpdateTempRows(s);
			foreach (DriveSample drife in s.Drives)
			{
				if (_metricRows.TryGetValue("drive:" + drife.Name, out var value4))
				{
					value4.Set(drife.Percent);
					value4.SetLabel(drife.Name + "(" + FormatSize(drife.TotalGb) + ")");
				}
			}
		});
	}

	private void UpdateTempRows(Sample s)
	{
		if (_metricRows.TryGetValue("cpu", out var value))
		{
			MetricRow metricRow = value;
			double? cpuTemp = s.CpuTemp;
			object text;
			if (cpuTemp.HasValue)
			{
				double valueOrDefault = cpuTemp.GetValueOrDefault();
				text = $"{valueOrDefault:0}℃";
			}
			else
			{
				text = "";
			}
			cpuTemp = s.CpuTemp;
			Brush color;
			if (cpuTemp.HasValue)
			{
				double valueOrDefault2 = cpuTemp.GetValueOrDefault();
				color = TemperatureBrush(valueOrDefault2);
			}
			else
			{
				color = Theme.Faint;
			}
			metricRow.SetMeta((string)text, color);
		}
		if (_metricRows.TryGetValue("gpu", out var value2))
		{
			double? cpuTemp = s.GpuTemp;
			object obj;
			if (cpuTemp.HasValue)
			{
				double valueOrDefault3 = cpuTemp.GetValueOrDefault();
				obj = $"{valueOrDefault3:0}℃";
			}
			else
			{
				obj = "";
			}
			string text2 = (string)obj;
			cpuTemp = s.GpuPower;
			if (cpuTemp.HasValue)
			{
				double valueOrDefault4 = cpuTemp.GetValueOrDefault();
				text2 = ((text2.Length > 0) ? (text2 + " · ") : "") + $"{valueOrDefault4:0}W";
			}
			MetricRow metricRow2 = value2;
			string text3 = text2;
			cpuTemp = s.GpuTemp;
			Brush color2;
			if (cpuTemp.HasValue)
			{
				double valueOrDefault5 = cpuTemp.GetValueOrDefault();
				color2 = TemperatureBrush(valueOrDefault5);
			}
			else
			{
				color2 = Theme.Faint;
			}
			metricRow2.SetMeta(text3, color2);
		}
	}

	private static Brush TemperatureBrush(double celsius)
	{
		if (!(celsius >= 85.0))
		{
			if (!(celsius >= 75.0))
			{
				return Theme.Text;
			}
			return Theme.Warn;
		}
		return Theme.Danger;
	}

	private void ScheduleProbe(int delayMs)
	{
		if (_cfg.Services.Count == 0)
		{
			if (_servicesDot != null)
			{
				_servicesDot.Fill = Theme.DotOff;
			}
			return;
		}
		DispatcherTimer timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(Math.Max(1, delayMs))
		};
		timer.Tick += delegate
		{
			timer.Stop();
			List<ServiceItem> items = _cfg.Services.ToList();
			Task.Run(() => ServiceRunner.Probe(items)).ContinueWith(delegate(Task<bool[]> task)
			{
				if (task.Status == TaskStatus.RanToCompletion)
				{
					bool[] states = task.Result;
					base.Dispatcher.BeginInvoke((Action)delegate
					{
						ApplyServices(states);
					});
				}
			});
		};
		timer.Start();
	}

	/// <summary>按探测结果刷每一行服务的开关点。</summary>
	private void ApplyServices(bool[] states)
	{
		int running = 0;
		for (int i = 0; i < _serviceRows.Count; i++)
		{
			bool on = i < states.Length && states[i];
			if (on)
			{
				running++;
			}
			_serviceRows[i].SetOn(on);
		}
		if (_servicesDot != null)
		{
			_servicesDot.Fill = (running > 0 ? Theme.Accent : Theme.DotOff);
		}
	}

	// ------------------------------------------------------------ 服务条目的增删改

	/// <summary>按当前配置重建服务行（新增/编辑/删除之后调用）。</summary>
	private void RebuildServiceRows()
	{
		if (_servicesSection == null)
		{
			return;
		}
		_servicesSection.Children.Clear();
		_serviceRows.Clear();

		for (int i = 0; i < _cfg.Services.Count; i++)
		{
			int index = i;
			ServiceItem item = _cfg.Services[i];
			ServiceRow row = new ServiceRow(item.Name, hasExtra: false)
			{
				Background = Brushes.Transparent
			};
			row.StartClicked += delegate
			{
				ServiceRunner.Start(item);
				_hint.Text = "正在启动：" + item.Name;
				ScheduleProbe(1500);
			};
			row.StopClicked += delegate
			{
				ServiceRunner.Stop(item);
				_hint.Text = "已停止：" + item.Name;
				ScheduleProbe(800);
			};
			row.MouseRightButtonUp += delegate
			{
				ShowServiceMenu(index, row);
			};
			_serviceRows.Add(row);
			_servicesSection.Children.Add(row);
		}

		if (_cfg.Services.Count == 0)
		{
			_servicesSection.Children.Add(new TextBlock
			{
				Text = "还没有服务。点右上角「+ 新增」添加一条（名字 + 启动命令 + 进程名）。",
				FontFamily = Theme.Ui,
				FontSize = 9.5,
				Foreground = Theme.Faint,
				Margin = new Thickness(14.0, 2.0, 0.0, 0.0),
				TextWrapping = TextWrapping.Wrap
			});
		}

		ScheduleProbe(0);
	}

	private void AddServiceClicked()
	{
		ServiceEditor editor = new ServiceEditor(new ServiceItem());
		if (editor.ShowDialog() != true)
		{
			return;
		}
		_cfg.Services.Add(editor.Result);
		_cfg.Save();
		RebuildServiceRows();
		_hint.Text = "已添加服务：" + editor.Result.Name;
	}

	private void ShowServiceMenu(int index, ServiceRow row)
	{
		if (index < 0 || index >= _cfg.Services.Count)
		{
			return;
		}
		new MiniMenu().Add("编辑…", delegate
		{
			EditService(index);
		}).AddSeparator().Add("删除", delegate
		{
			DeleteService(index);
		}).Show(row);
	}

	private void EditService(int index)
	{
		if (index < 0 || index >= _cfg.Services.Count)
		{
			return;
		}
		ServiceEditor editor = new ServiceEditor(_cfg.Services[index]);
		if (editor.ShowDialog() != true)
		{
			return;
		}
		_cfg.Services[index] = editor.Result;
		_cfg.Save();
		RebuildServiceRows();
		_hint.Text = "已保存服务：" + editor.Result.Name;
	}

	private void DeleteService(int index)
	{
		if (index < 0 || index >= _cfg.Services.Count)
		{
			return;
		}
		ServiceItem item = _cfg.Services[index];
		ConfirmDialog dialog = new ConfirmDialog("删除服务", "把「" + item.Name + "」从服务列表移除？\n\n不会结束正在运行的进程。", "删除");
		if (dialog.ShowDialog() != true)
		{
			return;
		}
		_cfg.Services.RemoveAt(index);
		_cfg.Save();
		RebuildServiceRows();
		_hint.Text = "已移除：" + item.Name;
	}


	private void ReloadStartup()
	{
		_startupList.Children.Clear();
		List<StartupItem> list;
		try
		{
			list = StartupStore.Load();
		}
		catch
		{
			list = new List<StartupItem>();
		}
		foreach (StartupItem item in list)
		{
			StartupItem captured = item;
			StartupRow row = new StartupRow(item.Name, item.Source, item.Enabled);
			row.Toggled += delegate(bool enabled)
			{
				string text = StartupStore.SetEnabled(captured, enabled);
				if (text != null)
				{
					_hint.Text = "写入失败：" + text;
					Log.Write("startup: 切换失败 " + captured.Name + " :: " + text);
					ReloadStartup();
				}
				else
				{
					_hint.Text = captured.Name + " 已" + (enabled ? "启用" : "禁用");
				}
			};
			_startupList.Children.Add(row);
			row.MouseRightButtonUp += delegate(object _, MouseButtonEventArgs e)
			{
				e.Handled = true;
				ShowStartupItemMenu(row, captured);
			};
		}
		if (list.Count == 0)
		{
			_startupList.Children.Add(new TextBlock
			{
				Text = "没有启动项",
				FontFamily = Theme.Ui,
				FontSize = 11.0,
				Foreground = Theme.Faint
			});
		}

		_startupScroll?.ScrollToTop();
	}

	private void ShowStartupAddMenu(UIElement anchor)
	{
		new MiniMenu().Add("注册表 Run（推荐）", AddStartupToRun).Add("启动文件夹", AddStartupToFolder).AddSeparator()
			.Add("打开启动文件夹", delegate
			{
				try
				{
					Process.Start("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.Startup));
				}
				catch (Exception ex)
				{
					Log.Ex(ex);
				}
			})
			.Show(anchor, PlacementMode.Bottom);
	}

	private void AddStartupToRun()
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "选要开机启动的程序",
			Filter = "程序与快捷方式|*.exe;*.lnk;*.bat;*.cmd;*.url|所有文件|*.*",
			CheckFileExists = true
		};
		if (openFileDialog.ShowDialog() == true)
		{
			AppEditor appEditor = new AppEditor(new AppItem
			{
				Name = System.IO.Path.GetFileNameWithoutExtension(openFileDialog.FileName),
				Target = openFileDialog.FileName
			});
			if (appEditor.ShowDialog() == true)
			{
				string text = StartupStore.AddRun(appEditor.Result.Name, appEditor.Result.Target, appEditor.Result.Args);
				_hint.Text = ((text == null) ? ("已新增启动项 " + appEditor.Result.Name) : ("新增失败：" + text));
				ReloadStartup();
			}
		}
	}

	private void AddStartupToFolder()
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "选要开机启动的程序",
			Filter = "程序与快捷方式|*.exe;*.lnk;*.bat;*.cmd;*.url|所有文件|*.*",
			CheckFileExists = true
		};
		if (openFileDialog.ShowDialog() == true)
		{
			AppEditor appEditor = new AppEditor(new AppItem
			{
				Name = System.IO.Path.GetFileNameWithoutExtension(openFileDialog.FileName),
				Target = openFileDialog.FileName
			});
			if (appEditor.ShowDialog() == true)
			{
				string text = StartupStore.AddLink(appEditor.Result.Target, appEditor.Result.Name, appEditor.Result.Args);
				_hint.Text = ((text == null) ? ("已放进启动文件夹：" + appEditor.Result.Name) : ("新增失败：" + text));
				ReloadStartup();
			}
		}
	}

	private void ShowStartupItemMenu(UIElement anchor, StartupItem item)
	{
		new MiniMenu().Add(item.Enabled ? "禁用" : "启用", delegate
		{
			string text = StartupStore.SetEnabled(item, !item.Enabled);
			if (text != null)
			{
				_hint.Text = "写入失败：" + text;
			}
			ReloadStartup();
		}).AddSeparator().Add("移除…", delegate
		{
			if (new ConfirmDialog("移除启动项", "把「" + item.Name + "」从开机启动里移除？\n\n" + item.Command, "移除").ShowDialog() == true)
			{
				string text = StartupStore.Remove(item);
				_hint.Text = ((text == null) ? ("已移除 " + item.Name) : ("移除失败：" + text));
				ReloadStartup();
			}
		})
			.Show(anchor);
	}

	private void Launch(AppItem item)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(item.Target) || !File.Exists(item.Target))
			{
				_hint.Text = "未找到：" + item.Name;
				return;
			}
			Process.Start(new ProcessStartInfo(item.Target, item.Args)
			{
				UseShellExecute = true,
				WorkingDirectory = (System.IO.Path.GetDirectoryName(item.Target) ?? "")
			});
		}
		catch (Exception ex)
		{
			_hint.Text = "启动失败：" + ex.Message;
		}
	}

	private static void OpenUrl(string url)
	{
		try
		{
			Process.Start(new ProcessStartInfo(url)
			{
				UseShellExecute = true
			});
		}
		catch
		{
		}
	}

	private static string FormatSize(double gb)
	{
		CultureInfo invariantCulture = CultureInfo.InvariantCulture;
		if (!(gb >= 1024.0))
		{
			return Math.Round(gb).ToString("0", invariantCulture) + "G";
		}
		return (gb / 1024.0).ToString("0.#", invariantCulture) + "T";
	}

	internal void Quit()
	{
		Close();
	}

	/// <summary>托盘「重新载入配置」：把 config.json 的改动应用回来，不重启进程。</summary>
	internal int PanelWidth => _cfg.Width;

	/// <summary>打开可视化设置窗口。</summary>
	internal void OpenSettings()
	{
		var window = new SettingsWindow(this, _cfg);
		window.ShowDialog();
	}

	/// <summary>把设置窗口里的草稿应用到运行中的面板。</summary>
	internal void ApplySettings(Config draft, bool save = true)
	{
		bool drivesChanged = !(_cfg.Drives ?? new List<string>()).SequenceEqual(draft.Drives ?? new List<string>());
		bool refreshChanged = _cfg.RefreshMs != draft.RefreshMs;
		bool themeChanged = !string.Equals(_cfg.Theme, draft.Theme, StringComparison.OrdinalIgnoreCase);
		bool layerChanged = _cfg.DesktopLayer != draft.DesktopLayer;
		bool acrylicChanged = _cfg.Acrylic != draft.Acrylic;
		bool noActivateChanged = _cfg.NoActivate != draft.NoActivate;

		_cfg.Width = draft.Width;
		_cfg.RefreshMs = draft.RefreshMs;
		_cfg.Theme = draft.Theme;
		_cfg.Acrylic = draft.Acrylic;
		_cfg.ShowClock = draft.ShowClock;
		_cfg.ClockSeconds = draft.ClockSeconds;
		_cfg.DesktopLayer = draft.DesktopLayer;
		_cfg.NoActivate = draft.NoActivate;
		_cfg.Drives = draft.Drives;
		_cfg.Launcher = draft.Launcher;
		_cfg.Services = draft.Services;
		_cfg.Usage = draft.Usage;
		if (save) _cfg.Save();

		// 盘符变了：先重建行，再按新盘重建引擎
		if (drivesChanged)
		{
			RebuildDriveRows();
		}

		// 采样参数或盘符变了就重建指标引擎：先建新的再丢旧的，避免中途失败留下已释放实例
		if (refreshChanged || drivesChanged)
		{
			try
			{
				var next = new MetricsEngine(_cfg.RefreshMs, _cfg.Drives);
				next.Sampled += OnSample;
				var old = _metrics;
				_metrics = next;
				old?.Dispose();
			}
			catch (Exception ex) { Log.Ex(ex); }
		}

		// 额度区：显隐 + 定时器 + 默认视图
		_mode = (string.IsNullOrWhiteSpace(_cfg.Usage.Mode) ? "go" : _cfg.Usage.Mode.Trim().ToLowerInvariant());
		if (_usageSection != null)
			_usageSection.Visibility = (_cfg.Usage.Enabled ? Visibility.Visible : Visibility.Collapsed);
		if (_cfg.Usage.Enabled)
		{
			if (_usageTimer != null && !_usageTimer.IsEnabled) _usageTimer.Start();
			ShowUsageBody(false);
			RefreshUsage();
			RefreshBalance();
		}
		else
		{
			_usageTimer?.Stop();
		}

		// 时钟
		if (_clock != null)
		{
			_clock.Visibility = (_cfg.ShowClock ? Visibility.Visible : Visibility.Collapsed);
			_clock.Text = DateTime.Now.ToString(_cfg.ClockSeconds > 0 ? "HH:mm:ss" : "HH:mm");
		}

		Width = Math.Clamp(_cfg.Width, 220, 600);
		_appPanel?.Reload();
		RebuildServiceRows();
		ReloadStartup();
		_layer?.Place();
		ApplyResponsiveLayout();

		if (themeChanged || layerChanged || acrylicChanged || noActivateChanged)
			_hint.Text = "已保存；主题 / 桌面层 / 亚克力 / 不抢焦点需重启面板";
		else
			_hint.Text = "设置已保存";
	}

	internal void ReloadFromConfig()
	{
		try
		{
			Config fresh = Config.Load();
			ApplySettings(fresh, save: false);   // 复用同一套同步逻辑；Load 已经读过了，不必再写一次
			_hint.Text = "配置已重新载入";
		}
		catch (Exception ex)
		{
			Log.Ex(ex);
			_hint.Text = "配置载入失败，详情见日志";
		}
	}

	/// <summary>托盘里切换面板宽度（DIP）。</summary>
	internal void SetPanelWidth(int width)
	{
		if (width < 220 || width > 600)
		{
			return;
		}
		_cfg.Width = width;
		_cfg.Save();
		Width = width;
		_layer?.Place();
		ApplyResponsiveLayout();
		_hint.Text = "面板宽度：" + width;
	}

	internal void ReloadStartupItems()
	{
		ReloadStartup();
	}
}