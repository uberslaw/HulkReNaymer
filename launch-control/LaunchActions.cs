using System.Diagnostics;
using System.IO;
using System.Text;
using LaunchControl.Standard.Host;

namespace HulkReNaymer.LaunchControl;

static class LaunchActions
{
    static readonly object Gate = new();
    static bool _busy;

    public static string FindRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "HulkReNaymer.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }
        return Directory.GetCurrentDirectory();
    }

    public static string AppProject(string root) =>
        Path.Combine(root, "src", "HulkReNaymer.App", "HulkReNaymer.App.csproj");

    public static string ExePath(string root, string configuration) =>
        Path.Combine(root, "src", "HulkReNaymer.App", "bin", configuration, "net8.0-windows", "HulkReNaymer.exe");

    public static string ExeDirectory(string root, string configuration) =>
        Path.GetDirectoryName(ExePath(root, configuration))!;

    public static IReadOnlyList<int> FindRunningPids(string root)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ExePath(root, "Release"),
            ExePath(root, "Debug")
        };
        var pids = new List<int>();
        foreach (var proc in Process.GetProcessesByName("HulkReNaymer"))
        {
            try
            {
                var path = proc.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path) && targets.Contains(path))
                    pids.Add(proc.Id);
            }
            catch
            {
                /* MainModule can throw for other sessions / bitness */
            }
            finally
            {
                proc.Dispose();
            }
        }
        return pids;
    }

    public static void Rebuild(LaunchControlWindow window, string root, string configuration) =>
        RunExclusive(window, $"Rebuild {configuration}", () =>
        {
            EnsureSdk(window);
            window.AppendLog($"dotnet build HulkReNaymer.sln -c {configuration}");
            var (exit, lines) = RunLogged(
                "dotnet",
                $"build \"{Path.Combine(root, "HulkReNaymer.sln")}\" -c {configuration} --nologo",
                root);
            foreach (var line in lines)
                window.AppendLog(line);
            var exe = ExePath(root, configuration);
            if (exit != 0)
            {
                window.AppendLog($"Rebuild {configuration} failed (exit {exit}).", "ERROR");
                return;
            }
            if (!File.Exists(exe))
            {
                window.AppendLog($"Build reported success but {exe} is missing.", "ERROR");
                return;
            }
            window.AppendLog($"Rebuild {configuration} ok → {exe}", "OK");
        });

    public static void RunApp(LaunchControlWindow window, string root, string configuration)
    {
        var exe = ExePath(root, configuration);
        if (!File.Exists(exe))
        {
            window.AppendLog($"{configuration} exe is missing. Rebuild {configuration} first.", "ERROR");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe),
            ErrorDialog = false
        });
        window.AppendLog($"Started {configuration}: {exe}", "OK");
    }

    public static void OpenCli(LaunchControlWindow window, string root)
    {
        var releaseDir = ExeDirectory(root, "Release");
        var debugDir = ExeDirectory(root, "Debug");
        var pathPrefix = string.Join(";", new[] { releaseDir, debugDir }.Where(Directory.Exists));
        var hint = new StringBuilder();
        hint.Append("echo. & echo HulkReNaymer CLI & echo.");
        hint.Append(" & echo   HulkReNaymer.exe [file-or-folder ...]   open those items in the app");
        hint.Append(" & echo   dotnet test                             run engine tests");
        hint.Append(" & echo   dotnet run --project src\\HulkReNaymer.App");
        hint.Append(" & echo.");
        hint.Append($" & echo Repo: {root}");
        hint.Append($" & echo Release: {releaseDir}");
        hint.Append($" & echo Debug:   {debugDir}");
        hint.Append(" & echo.");

        var comspec = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        var pathSet = string.IsNullOrEmpty(pathPrefix) ? "" : $"set \"PATH={pathPrefix};%PATH%\" & ";
        Process.Start(new ProcessStartInfo
        {
            FileName = comspec,
            Arguments = $"/k title HulkReNaymer CLI & cd /d \"{root}\" & {pathSet}{hint}",
            UseShellExecute = true,
            WorkingDirectory = root,
            ErrorDialog = false
        });
        window.AppendLog($"Opened a command prompt at {root} (Release then Debug exe folders on PATH).", "OK");
    }

    static void RunExclusive(LaunchControlWindow window, string title, Action work)
    {
        lock (Gate)
        {
            if (_busy)
            {
                window.AppendLog("A rebuild is already running.", "WARN");
                return;
            }
            _busy = true;
        }

        Task.Run(() =>
        {
            try { work(); }
            catch (Exception ex) { window.AppendLog($"{title}: {ex.Message}", "ERROR"); }
            finally
            {
                lock (Gate) _busy = false;
            }
        });
    }

    static void EnsureSdk(LaunchControlWindow window)
    {
        var (exit, lines) = RunLogged("dotnet", "--list-sdks", Directory.GetCurrentDirectory(), maxLines: 20);
        if (exit != 0 || lines.Count == 0)
            throw new InvalidOperationException("The .NET SDK was not found. Install the .NET 8 SDK, then try again.");
        window.AppendLog("SDK: " + string.Join(" | ", lines.Take(3)));
    }

    static (int Exit, List<string> Lines) RunLogged(string fileName, string arguments, string workingDirectory, int maxLines = 250)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var lines = new List<string>();
        void Take(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            if (lines.Count < maxLines) lines.Add(line);
        }
        proc.OutputDataReceived += (_, e) => Take(e.Data);
        proc.ErrorDataReceived += (_, e) => Take(e.Data);
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        if (!proc.WaitForExit(10 * 60 * 1000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException($"{fileName} did not finish within 10 minutes.");
        }
        proc.WaitForExit();
        if (lines.Count >= maxLines)
            lines.Add($"(log capped at {maxLines} lines)");
        return (proc.ExitCode, lines);
    }
}
