using System.Text;

namespace HulkReNaymer;

/// <summary>
/// File-only crash log for the WPF app. WinExe has no console, so startup
/// exceptions must land on disk without a debugger.
/// </summary>
public static class CrashLog
{
    public const string AppFileName = "app-crash.log";

    public static string AppLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HulkReNaymer",
        "logs",
        AppFileName);

    public static void Write(string source, object? exceptionObject, string? path = null)
    {
        path ??= AppLogPath;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(path, Format(source, exceptionObject));
        }
        catch
        {
            // Never throw from the logger.
        }
    }

    public static string Format(string source, object? exceptionObject)
    {
        var line = new StringBuilder();
        line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        line.Append(" [FATAL] ").Append(source);
        if (exceptionObject is Exception ex)
        {
            line.Append(": ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            line.AppendLine().Append(ex);
        }
        else if (exceptionObject is not null)
        {
            line.Append(": ").Append(exceptionObject);
        }
        line.AppendLine();
        return line.ToString();
    }
}
