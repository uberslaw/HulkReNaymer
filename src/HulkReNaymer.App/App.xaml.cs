using System.Windows;
using System.Windows.Threading;
using HulkReNaymer;

namespace HulkReNaymer.App;

public partial class App : Application
{
    static App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write("AppDomain.UnhandledException", e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandled;
        base.OnStartup(e);
    }

    void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLog.Write("DispatcherUnhandledException", e.Exception);
        ShowCrashDialog(e.Exception);
        e.Handled = true;
        if (MainWindow is null)
            Shutdown(1);
    }

    static void ShowCrashDialog(Exception ex)
    {
        try
        {
            MessageBox.Show(
                "HulkReNaymer hit an error and wrote it to:" + Environment.NewLine +
                CrashLog.AppLogPath + Environment.NewLine + Environment.NewLine +
                ex.GetType().Name + ": " + ex.Message,
                "HulkReNaymer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // File already flushed; a dialog failure must not hide the log write.
        }
    }
}
