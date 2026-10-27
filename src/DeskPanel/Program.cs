using System.Windows;
using System.Windows.Threading;
using DeskPanel.Ui;

namespace DeskPanel;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\DeskPanel.SingleInstance", out bool created);
        if (!created) return;

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
            app.Run(new MainWindow());
            Log.Write("==== exit ====");
        }
        catch (Exception ex)
        {
            Log.Ex(ex);
        }
    }
}
