using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DeskPanel.Ui;

namespace DeskPanel;

internal static class Program
{
    /// <summary>已经有一个实例在跑时，用它来请求「打开设置」。</summary>
    private const string SettingsEventName = @"Local\DeskPanel.OpenSettings";

    [STAThread]
    private static void Main(string[] args)
    {
        // 诊断用：--qianfan-parse <json> 只跑解析器，不开窗口
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--qianfan-parse" && i + 1 < args.Length)
            {
                try
                {
                    string json = File.ReadAllText(args[i + 1]);
                    var snap = Metrics.QianfanCredits.Parse(json);
                    Console.WriteLine($"ok={snap.Ok} percent={snap.Percent} remaining={snap.Remaining} total={snap.Total} reset={snap.ResetText} periodStart={snap.PeriodStart} periodEnd={snap.PeriodEnd} err={snap.Error}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("parse failed: " + ex.Message);
                }
                return;
            }
        }

        // 诊断用：--qianfan-capture 只跑抓取，把 cURL 打到控制台
        if (args.Contains("--qianfan-capture"))
        {
            var result = Metrics.QianfanCapture.CaptureAsync(
                180, m => Console.WriteLine("[capture] " + m), CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine($"ok={result.Ok} url={result.Url} err={result.Error}");
            if (result.Ok) Console.WriteLine("CURL=" + result.Curl);
            return;
        }

        bool wantsSettings = false;
        foreach (string arg in args)
        {
            if (arg is "--settings" or "-settings" or "/settings") wantsSettings = true;
        }

        using var mutex = new Mutex(true, @"Local\DeskPanel.SingleInstance", out bool created);
        if (!created)
        {
            // 面板已在运行：把请求转发过去，别静默退出
            if (wantsSettings)
            {
                // 刚开机时另一个实例可能还在建事件，重试几次再放弃
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        if (EventWaitHandle.TryOpenExisting(SettingsEventName, out var handle))
                        {
                            handle.Set();
                            handle.Dispose();
                            return;
                        }
                    }
                    catch { }
                    Thread.Sleep(100);
                }
            }
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Ex(e.ExceptionObject as Exception ?? new Exception("unknown"));
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Ex(e.Exception);

        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        Ui.ScrollBars.Apply(app);
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Ex(e.Exception);
            e.Handled = true;
        };

        try
        {
            Log.Write("==== start ====");
            var window = new MainWindow();

            // 外部再执行一次 --settings 时，用命名事件唤起设置窗口；建不出来也不影响面板本身
            EventWaitHandle wake = null;
            try
            {
                wake = new EventWaitHandle(false, EventResetMode.AutoReset, SettingsEventName);
            }
            catch (Exception ex)
            {
                Log.Ex(ex);
            }

            if (wake != null)
            {
            var listener = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        if (!wake.WaitOne()) continue;
                    }
                    catch { return; }

                    try
                    {
                        app.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try { window.OpenSettings(); }
                            catch (Exception ex) { Log.Ex(ex); }
                        }));
                    }
                    catch
                    {
                        return;   // 程序正在退出，Dispatcher 已关闭
                    }
                }
            })
            {
                IsBackground = true,
                Name = "DeskPanel.SettingsListener",
            };
            listener.Start();
            }

            if (wantsSettings)
            {
                window.Loaded += (_, _) => window.OpenSettings();
            }

            app.Run(window);
            Log.Write("==== exit ====");
        }
        catch (Exception ex)
        {
            Log.Ex(ex);
        }
    }
}
