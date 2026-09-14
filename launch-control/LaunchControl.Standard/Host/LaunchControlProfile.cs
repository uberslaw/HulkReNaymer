using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;
using System.Text.Json;
using System.Windows;

namespace LaunchControl.Standard.Host;

public sealed record ExtraAction(string Title, Action<LaunchControlWindow> Run, string? Group = null);

/// <summary>Optional layout hints for Heimdall-style Launch Controls.</summary>
public sealed class ExtraActionLayout
{
    /// <summary>Groups rendered as Expanders starting collapsed (e.g. Diagnostics, Recovery, Logs).</summary>
    public IReadOnlyList<string> CollapsedGroups { get; init; } = [];
    /// <summary>Groups rendered as compact GroupBox + small buttons (Windows-services style).</summary>
    public IReadOnlyList<string> CompactGroups { get; init; } = [];
}

public sealed class ProcessFallbackSpec
{
    public required Func<string> WorkingDirectory { get; init; }
    public required Func<(string FileName, string Arguments)> StartInfo { get; init; }
    public string? PidFile { get; init; }
    /// <summary>
    /// GUI exe (WPF/WinForms): start detached, do not redirect stdio.
    /// Working directory is always the exe folder (Explorer behavior).
    /// Status/Stop use <see cref="FindRunningPids"/> when set.
    /// </summary>
    public bool DetachedGui { get; init; }
    public Func<IReadOnlyList<int>>? FindRunningPids { get; init; }
    /// <summary>Optional lines logged immediately before start (message, level).</summary>
    public Func<IReadOnlyList<(string Message, string Level)>>? LaunchNotes { get; init; }
}

public sealed class LaunchControlProfile
{
    public required string ProductId { get; init; }
    public required string ProductName { get; init; }
    public required string AppDataFolder { get; init; }
    public required string[] ServiceNames { get; init; }
    /// <summary>
    /// Product install folder. Used to list python.exe / pythonw.exe / pythonservice.exe
    /// whose path or command line is this product's .venv or venv (pywin32 DLL lock).
    /// Defaults to <see cref="ProcessFallback"/> working directory when omitted.
    /// </summary>
    public string? InstallRoot { get; init; }
    public string? HealthUrl { get; init; }
    public string? BrowserUrl { get; init; }
    public string[] LogPaths { get; init; } = [];
    public ProcessFallbackSpec? ProcessFallback { get; init; }
    public IReadOnlyList<ExtraAction> ExtraActions { get; init; } = [];
    public ExtraActionLayout? ExtraActionLayout { get; init; }
    public IReadOnlyDictionary<string, string>? DefaultColors { get; init; }
    public Func<string>? MetaText { get; init; }
    public string? DiagnosticsFlagPath { get; init; }
    /// <summary>Durable LC log (theme crashes, unhandled exceptions). Default: %LocalAppData%\{AppDataFolder}\logs\launch-control.log</summary>
    public string? CrashLogPath { get; init; }
    /// <summary>Python venv locker panel. Default off — set true only for Python products.</summary>
    public bool ShowVenvUi { get; init; }
    public bool ShowBrowserButton { get; init; } = true;
    public bool ShowRestartButton { get; init; } = true;
    public bool ShowStartStopButtons { get; init; } = true;
    /// <summary>When false, hide the chrome Refresh status button (use an ExtraAction instead).</summary>
    public bool ShowRefreshButton { get; init; } = true;
    /// <summary>When false, hide the chrome Follow logs button (use an ExtraAction instead).</summary>
    public bool ShowFollowLogsButton { get; init; } = true;
    /// <summary>
    /// When true and multiple <see cref="ServiceNames"/> exist, replace the single Start/Stop/Restart
    /// stack with a compact per-service GroupBox (Start / Stop / Restart row).
    /// </summary>
    public bool CompactServiceControls { get; init; }
    /// <summary>Ignored — log pane is always visible (no collapse chrome).</summary>
    public bool CollapseLogPaneByDefault { get; init; }
    public string? StartButtonText { get; init; }
    public string? StopButtonText { get; init; }
    public string? FooterText { get; init; }
    /// <summary>Replaces the default service/Python elevation blurb on first load.</summary>
    public IReadOnlyList<string>? StartupNotes { get; init; }
}

public static class AdminUtil
{
    public const string RestartAsAdminHint =
        "Click Restart as administrator (shield), leave that window open, then Open Launch Control from a card so children inherit the token.";

    public static bool IsAdministrator()
    {
        try
        {
            var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            var p = new System.Security.Principal.WindowsPrincipal(id);
            return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static void RequireAdministrator(string operation)
    {
        if (IsAdministrator()) return;
        throw new InvalidOperationException(
            $"{operation} needs administrator. {RestartAsAdminHint} Start/Stop/Install do not fail silently — they refuse until elevated.");
    }

    /// <summary>
    /// Relaunch this EXE with a UAC prompt. Returns true if a new process started
    /// (caller should shut down). Does nothing if already admin.
    /// </summary>
    public static bool TryRestartAsAdministrator()
    {
        if (IsAdministrator()) return false;

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            throw new InvalidOperationException("Cannot locate this executable to restart as administrator.");

        try
        {
            var started = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
                ErrorDialog = false
            });
            if (started is null)
                throw new InvalidOperationException("UAC elevation did not start a process (cancelled or blocked).");
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("UAC cancelled — still running without administrator rights.");
        }
    }
}

public static class ServiceRuntime
{
    public sealed record Info(bool Exists, string Name, string State, int ProcessId, int ExitCode);

    public static Info Query(string name)
    {
        try
        {
            using var sc = new ServiceController(name);
            _ = sc.Status;
            return new Info(true, name, sc.Status.ToString(), TryGetServiceProcessId(name), 0);
        }
        catch
        {
            return new Info(false, name, "", 0, 0);
        }
    }

    /// <summary>ServiceController does not expose PID — Win32_Service does.</summary>
    private static int TryGetServiceProcessId(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return 0;
        try
        {
            var escaped = serviceName.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("'", "\\'", StringComparison.Ordinal);
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT ProcessId FROM Win32_Service WHERE Name = '{escaped}'");
            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                var pid = Convert.ToInt32(obj["ProcessId"] ?? 0);
                if (pid > 0)
                    return pid;
            }
        }
        catch
        {
            /* WMI unavailable — leave PID unknown */
        }

        return 0;
    }

    /// <summary>
    /// Full exception chain including Win32 native codes. ServiceController
    /// otherwise surfaces only "Cannot stop 'X' service on computer '.'.".
    /// </summary>
    public static string FormatException(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is Win32Exception w32)
                parts.Add($"{e.GetType().Name} (Win32 {w32.NativeErrorCode}): {w32.Message}");
            else
                parts.Add($"{e.GetType().Name}: {e.Message}");
        }
        return parts.Count == 0 ? ex.GetType().Name : string.Join(" — ", parts);
    }

    public static void Start(string name)
    {
        using var sc = new ServiceController(name);
        if (sc.Status == ServiceControllerStatus.Running) return;
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// SCM Stop with a short wait, then WinSW <c>exe stop</c> and taskkill of
    /// the service PID if the wrapper is hung (StopPending / failed OnStop).
    /// </summary>
    public static void Stop(string name, string? installRoot = null, TimeSpan? timeout = null)
    {
        var wait = timeout ?? TimeSpan.FromSeconds(12);
        Exception? first = null;
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status == ServiceControllerStatus.Stopped) return;
            if (sc.Status != ServiceControllerStatus.StopPending)
                sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, wait);
            if (IsStopped(name)) return;
        }
        catch (Exception ex)
        {
            first = ex;
            if (IsStopped(name)) return;
        }

        TryWinswCliStop(name, installRoot);
        if (WaitUntilStopped(name, TimeSpan.FromSeconds(8))) return;

        var pid = TryGetServiceProcessId(name);
        if (pid > 0)
            TryTaskKill(pid);
        if (WaitUntilStopped(name, TimeSpan.FromSeconds(8))) return;

        var still = Query(name);
        var detail = first != null ? FormatException(first) : $"service still {still.State} PID {still.ProcessId}";
        throw new InvalidOperationException(
            $"Cannot stop '{name}' (state {still.State}, PID {still.ProcessId}): {detail}");
    }

    public static void Restart(string name, string? installRoot = null)
    {
        Stop(name, installRoot);
        Start(name);
    }

    private static bool IsStopped(string name)
    {
        var info = Query(name);
        return !info.Exists || info.State.Equals("Stopped", StringComparison.OrdinalIgnoreCase);
    }

    private static bool WaitUntilStopped(string name, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (IsStopped(name)) return true;
            Thread.Sleep(400);
        }
        return IsStopped(name);
    }

    private static string? TryGetServicePathName(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return null;
        try
        {
            var escaped = serviceName.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("'", "\\'", StringComparison.Ordinal);
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT PathName FROM Win32_Service WHERE Name = '{escaped}'");
            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                var path = (obj["PathName"]?.ToString() ?? "").Trim().Trim('"');
                if (path.Length > 0)
                    return path;
            }
        }
        catch
        {
            /* WMI unavailable */
        }
        return null;
    }

    private static void TryWinswCliStop(string name, string? installRoot)
    {
        var candidates = new List<string>();
        var pathName = TryGetServicePathName(name);
        if (!string.IsNullOrWhiteSpace(pathName) && pathName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            candidates.Add(pathName);
        if (!string.IsNullOrWhiteSpace(installRoot))
        {
            candidates.Add(Path.Combine(installRoot, "scripts", "winsw", name + ".exe"));
            candidates.Add(Path.Combine(installRoot, "scripts", "winsw", "WinSW.NET461.exe"));
        }

        foreach (var exe in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(exe)) continue;
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "stop",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
                });
                p?.WaitForExit(15000);
                return;
            }
            catch
            {
                // Next candidate or taskkill.
            }
        }
    }

    private static void TryTaskKill(int pid)
    {
        if (pid <= 0 || pid == Environment.ProcessId) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                Arguments = $"/F /PID {pid} /T",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit(8000);
        }
        catch
        {
            /* reported by caller if service still running */
        }
    }

    public static bool IsProcessAlive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch { return false; }
    }
}

public static class HealthClient
{
    // Health probes a host that Start/Stop/Install tears down. Reused pooled
    // sockets then throw IOException ("connection forcibly closed") on a
    // background HttpClient task — that must not become FATAL.
    private static readonly HttpClient Http = CreateClient();

    public sealed record Result(bool Ok, int Ms, string? Error, string? ProductVersion = null, string? MachineName = null);

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.Zero,
            ConnectTimeout = TimeSpan.FromSeconds(3)
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public static async Task<Result> GetAsync(string url, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(5));
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseContentRead, linked.Token)
                .ConfigureAwait(false);
            sw.Stop();
            if (!resp.IsSuccessStatusCode)
                return new Result(false, (int)sw.ElapsedMilliseconds, $"HTTP {(int)resp.StatusCode}");

            string? productVersion = null;
            string? machineName = null;
            try
            {
                var json = await resp.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
                productVersion = TryReadJsonString(json, "productVersion");
                machineName = TryReadJsonString(json, "machineName");
            }
            catch
            {
                /* body optional */
            }

            return new Result(true, (int)sw.ElapsedMilliseconds, null, productVersion, machineName);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new Result(false, (int)sw.ElapsedMilliseconds, UnwrapProbeError(ex));
        }
    }

    private static string? TryReadJsonString(string json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyName))
            return null;
        // Lightweight parse — health payload is a small flat object.
        var key = "\"" + propertyName + "\"";
        var idx = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var colon = json.IndexOf(':', idx + key.Length);
        if (colon < 0) return null;
        var q1 = json.IndexOf('"', colon + 1);
        if (q1 < 0) return null;
        var q2 = json.IndexOf('"', q1 + 1);
        if (q2 <= q1) return null;
        var value = json[(q1 + 1)..q2].Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static bool IsExpectedDisconnect(Exception ex)
    {
        foreach (var inner in Flatten(ex))
        {
            if (inner is IOException or System.Net.Sockets.SocketException or HttpRequestException
                or ObjectDisposedException)
                return true;
            var msg = inner.Message ?? "";
            if (msg.Contains("forcibly closed", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("transport connection", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("connection was aborted", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("connection refused", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string UnwrapProbeError(Exception ex)
    {
        var inner = Flatten(ex).LastOrDefault() ?? ex;
        return string.IsNullOrWhiteSpace(inner.Message) ? inner.GetType().Name : inner.Message;
    }

    private static IEnumerable<Exception> Flatten(Exception ex)
    {
        switch (ex)
        {
            case AggregateException agg:
                foreach (var inner in agg.Flatten().InnerExceptions)
                {
                    foreach (var nested in Flatten(inner))
                        yield return nested;
                }
                yield break;
            case Exception { InnerException: { } inner } when inner != ex:
                yield return ex;
                foreach (var nested in Flatten(inner))
                    yield return nested;
                yield break;
            default:
                yield return ex;
                yield break;
        }
    }
}

public static class LogTailer
{
    public static IReadOnlyList<string> ReadNewLines(string path, ref long offset, int maxLines = 80)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return [];
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (offset > fs.Length)
                offset = 0;
            fs.Seek(offset, SeekOrigin.Begin);
            using var sr = new StreamReader(fs);
            var text = sr.ReadToEnd();
            offset = fs.Position;
            var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None)
                .Where(l => l.Length > 0)
                .ToList();
            if (lines.Count > maxLines)
                lines = lines.TakeLast(maxLines).ToList();
            return lines;
        }
        catch
        {
            return [];
        }
    }

    public static bool IsImportant(string line) =>
        line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
        || line.Contains("WARN", StringComparison.OrdinalIgnoreCase)
        || line.Contains("FAIL", StringComparison.OrdinalIgnoreCase)
        || line.Contains("EXCEPTION", StringComparison.OrdinalIgnoreCase);
}

public static class ProcessUtil
{
    public static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    public static void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (Directory.Exists(path) || File.Exists(path))
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            return;
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
    }

    public static void StartPowerShell(string scriptPath, string workingDirectory, bool elevate, params string[] extraArgs) =>
        StartPowerShellCore(scriptPath, workingDirectory, elevate, wait: false, logPath: null, extraArgs);

    public static int StartElevatedPowerShellWait(string scriptPath, string workingDirectory, params string[] extraArgs) =>
        StartPowerShellCore(scriptPath, workingDirectory, elevate: true, wait: true, logPath: null, extraArgs);

    public static int StartElevatedPowerShellWait(string scriptPath, string workingDirectory, string[] extraArgs, string? logPath) =>
        StartPowerShellCore(scriptPath, workingDirectory, elevate: true, wait: true, logPath, extraArgs);

    public static void StartElevatedPowerShell(string scriptPath, string workingDirectory, params string[] extraArgs) =>
        StartPowerShell(scriptPath, workingDirectory, elevate: true, extraArgs);

    private static int StartPowerShellCore(
        string scriptPath,
        string workingDirectory,
        bool elevate,
        bool wait,
        string? logPath,
        params string[] extraArgs)
    {
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("Script not found.", scriptPath);

        var work = string.IsNullOrWhiteSpace(workingDirectory)
            ? Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory
            : workingDirectory;

        // Verb=runas + UseShellExecute ignores WorkingDirectory and cannot redirect
        // stderr. If this process is already admin, CreateProcess so the child
        // inherits the token — no second UAC. Do not hide interactive Setup UIs
        // (wait=false); hide only waited console installers that log to a file.
        var needUac = elevate && !AdminUtil.IsAdministrator();
        AppendLaunchPreamble(logPath, scriptPath, work, needUac);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = BuildPowerShellArguments(scriptPath, extraArgs ?? []),
            WorkingDirectory = work,
            ErrorDialog = false
        };
        if (needUac)
        {
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            psi.WindowStyle = ProcessWindowStyle.Normal;
        }
        else
        {
            psi.UseShellExecute = false;
            if (wait)
            {
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.Environment["LC_INSTALL_NOPAUSE"] = "1";
            }
            else
            {
                psi.CreateNoWindow = false;
                psi.WindowStyle = ProcessWindowStyle.Normal;
            }
        }

        try
        {
            var proc = Process.Start(psi);
            if (proc is null)
                throw new InvalidOperationException("UAC elevation did not start a process (cancelled or blocked).");
            if (!wait)
                return 0;

            string? stdout = null;
            string? stderr = null;
            Task<string>? stdoutTask = null;
            Task<string>? stderrTask = null;
            if (psi.RedirectStandardOutput)
                stdoutTask = proc.StandardOutput.ReadToEndAsync();
            if (psi.RedirectStandardError)
                stderrTask = proc.StandardError.ReadToEndAsync();
            proc.WaitForExit();
            if (stdoutTask is not null)
                stdout = stdoutTask.GetAwaiter().GetResult();
            if (stderrTask is not null)
                stderr = stderrTask.GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(stderr))
                AppendLogFile(logPath, "--- powershell host stderr ---" + Environment.NewLine + stderr);
            if (!string.IsNullOrWhiteSpace(stdout))
                AppendLogFile(logPath, stdout);
            return proc.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("UAC cancelled - Windows service was not installed or changed.");
        }
    }

    private static void AppendLaunchPreamble(string? logPath, string scriptPath, string work, bool needUac)
    {
        if (string.IsNullOrWhiteSpace(logPath)) return;
        AppendLogFile(logPath,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [INFO] Launch Control starting powershell -File \"{scriptPath}\" cwd=\"{work}\" uac={needUac}");
    }

    private static void AppendLogFile(string? logPath, string text)
    {
        if (string.IsNullOrWhiteSpace(logPath) || string.IsNullOrEmpty(text)) return;
        try
        {
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            if (!text.EndsWith('\n'))
                text += Environment.NewLine;
            File.AppendAllText(logPath, text);
        }
        catch { }
    }

    private static string BuildPowerShellArguments(string scriptPath, string[] extraArgs)
    {
        var parts = new List<string>
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            QuoteProcessArg(Path.GetFullPath(scriptPath))
        };
        foreach (var a in extraArgs)
            parts.Add(QuoteProcessArg(a));
        return string.Join(" ", parts);
    }

    private static string QuoteProcessArg(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";
        if (value.IndexOfAny([' ', '\t', '"']) < 0)
            return value;
        // Windows argv quoting: doubled inner quotes, not C-style backslash escapes.
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public static string? FindPython(string root)
    {
        try
        {
            var cfgPath = Path.Combine(root, "windows_service", "config.json");
            if (File.Exists(cfgPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(cfgPath));
                if (doc.RootElement.TryGetProperty("python_executable", out var el))
                {
                    var configured = el.GetString();
                    if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                        return configured;
                }
            }
        }
        catch { }

        foreach (var rel in new[] { @"venv\Scripts\python.exe", @".venv\Scripts\python.exe" })
        {
            var p = Path.Combine(root, rel);
            if (File.Exists(p)) return p;
        }

        foreach (var c in new[]
                 {
                     @"C:\Python314\python.exe", @"C:\Python313\python.exe",
                     @"C:\Python312\python.exe", @"C:\Python311\python.exe"
                 })
        {
            if (File.Exists(c)) return c;
        }

        try
        {
            var fromPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in fromPath.Split(Path.PathSeparator))
            {
                var p = Path.Combine(dir, "python.exe");
                if (File.Exists(p)) return p;
            }
        }
        catch { }

        return null;
    }
}

public static class LaunchControlApp
{
    public static void Run(Application app, LaunchControlProfile profile)
    {
        var logPath = string.IsNullOrWhiteSpace(profile.CrashLogPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                profile.AppDataFolder,
                "logs",
                "launch-control.log")
            : profile.CrashLogPath!;
        LcHostLog.Initialize(logPath, profile.ProductName);
        LcHostLog.AttachUnhandled(app);

        Theme.ThemeService.InitializeForApp(profile.AppDataFolder, profile.DefaultColors);
        Compat.LcCompat.Write(
            AppContext.BaseDirectory,
            profile.ProductId,
            profile.ProductName,
            profile.AppDataFolder);
        var win = new LaunchControlWindow(profile);
        LcHostLog.SetUiSink(win.AppendLogPane, win.Dispatcher);
        app.MainWindow = win;
        win.Show();
    }
}
