using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace LaunchControl.Standard.Host;

/// <summary>
/// Durable Launch Control log. Survives a Theme crash because the file is flushed
/// before any MessageBox or shutdown.
/// </summary>
public static class LcHostLog
{
    private static readonly object Gate = new();
    private static string _logPath = "";
    private static string _product = "Launch Control";
    private static Action<string, string>? _ui;
    private static Dispatcher? _dispatcher;
    private static bool _hooks;

    public static string LogPath
    {
        get
        {
            lock (Gate)
                return _logPath;
        }
    }

    public static void Initialize(string logPath, string productName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        var dir = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        lock (Gate)
        {
            _logPath = logPath;
            _product = string.IsNullOrWhiteSpace(productName) ? "Launch Control" : productName;
        }

        Write("INFO", $"{_product} starting. {LcBuildInfo.Stamp}. Log={logPath}", null, toUi: false);
    }

    public static void SetUiSink(Action<string, string>? sink, Dispatcher? dispatcher = null)
    {
        lock (Gate)
        {
            _ui = sink;
            _dispatcher = dispatcher ?? Application.Current?.Dispatcher;
        }
    }

    public static void AttachUnhandled(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (_hooks)
            return;
        _hooks = true;

        app.DispatcherUnhandledException += (_, e) =>
        {
            if (HealthClient.IsExpectedDisconnect(e.Exception))
            {
                Warn("Health probe connection closed (service stopping or restarting): " + e.Exception.Message);
                e.Handled = true;
                return;
            }
            Fatal(e.Exception, "DispatcherUnhandledException", shutdown: false);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception
                     ?? new InvalidOperationException(e.ExceptionObject?.ToString() ?? "Unknown unhandled exception");
            Fatal(ex, "AppDomain.UnhandledException", shutdown: e.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            if (HealthClient.IsExpectedDisconnect(e.Exception))
            {
                Warn("Health probe connection closed (service stopping or restarting): "
                     + InnermostMessage(e.Exception));
                return;
            }
            Fatal(e.Exception, "TaskScheduler.UnobservedTaskException", shutdown: false);
        };
    }

    public static void Info(string message) => Write("INFO", message, null, toUi: true);

    public static void Warn(string message) => Write("WARN", message, null, toUi: true);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", message, ex, toUi: true);

    /// <summary>File only — used by the console pane so we do not echo twice.</summary>
    public static void WriteFile(string level, string message) =>
        Write(level, message, null, toUi: false);

    public static void Fatal(Exception ex, string source, bool shutdown)
    {
        var path = LogPath;
        Write("FATAL", $"{source}: {ex.GetType().Name}: {ex.Message}", ex, toUi: true);
        ShowFatalDialog(path, ex, shutdown);
    }

    private static string InnermostMessage(Exception ex)
    {
        var current = ex;
        while (current.InnerException is not null)
            current = current.InnerException;
        return string.IsNullOrWhiteSpace(current.Message) ? current.GetType().Name : current.Message;
    }

    private static void ShowFatalDialog(string path, Exception ex, bool shutdown)
    {
        var text =
            $"{_product} hit an error and wrote it to:{Environment.NewLine}{path}{Environment.NewLine}{Environment.NewLine}" +
            $"{ex.GetType().Name}: {ex.Message}{Environment.NewLine}{Environment.NewLine}" +
            (shutdown
                ? "The process may still exit. Open that log before restarting."
                : "The window should stay open. Theme / glass errors are logged instead of a silent exit.");

        try
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() =>
                    MessageBox.Show(text, $"{_product} Launch Control", MessageBoxButton.OK, MessageBoxImage.Error));
            }
            else
            {
                MessageBox.Show(text, $"{_product} Launch Control", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch
        {
            // Logging already flushed; a dialog failure must not hide the file write.
        }
    }

    private static void Write(string level, string message, Exception? ex, bool toUi)
    {
        string path;
        Action<string, string>? ui;
        Dispatcher? dispatcher;
        lock (Gate)
        {
            path = _logPath;
            ui = _ui;
            dispatcher = _dispatcher;
        }

        var line = new StringBuilder();
        line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        line.Append(" [").Append(level).Append("] ").Append(message);
        if (ex is not null)
        {
            line.AppendLine().Append(ex);
        }

        var text = line.ToString();
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                lock (Gate)
                {
                    using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var sw = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    sw.WriteLine(text);
                    sw.Flush();
                    fs.Flush(flushToDisk: true);
                }
            }
            catch
            {
                // Never throw from the logger.
            }
        }

        if (!toUi || ui is null)
            return;

        try
        {
            if (dispatcher is null || dispatcher.CheckAccess())
                ui(message, level);
            else
                dispatcher.BeginInvoke(() => ui(message, level));
        }
        catch
        {
            // Pane may already be tearing down.
        }
    }
}

public static class LcBuildInfo
{
    public static string Stamp
    {
        get
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                exe = typeof(LcBuildInfo).Assembly.Location;
            var ver = typeof(LcBuildInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            DateTime built;
            try { built = File.GetLastWriteTime(exe); }
            catch { built = DateTime.Now; }
            return $"v{ver}  built {built:yyyy-MM-dd HH:mm}";
        }
    }
}
