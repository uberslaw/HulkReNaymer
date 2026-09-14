using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LaunchControl.Standard.Theme;

namespace LaunchControl.Standard.Host;

public partial class LaunchControlWindow : Window, IThemeHighlightHost
{
    private readonly LaunchControlProfile _profile;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, long> _logOffsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ThemeRoleHighlightAdorner> _adorners = new();
    private ThemeSettingsWindow? _themeWindow;
    private bool _themePickMode;
    private bool _followLogs;
    private bool _busy;
    private Process? _child;
    private DateTime? _startedAt;
    private bool _logReady;
    private int _statusGate;

    public LaunchControlWindow(LaunchControlProfile profile)
    {
        _profile = profile;
        InitializeComponent();
        Title = $"{profile.ProductName} Launch Control";
        TitleText.Text = $"{profile.ProductName} Launch Control  ·  {LcBuildInfo.Stamp}";
        ApplyHostChrome();
        BuildExtraButtons();

        Loaded += async (_, _) =>
        {
            try
            {
                AppendLog($"{profile.ProductName} Launch Control ready. {LcBuildInfo.Stamp}");
                AppendLog($"Crash / theme log: {LcHostLog.LogPath}");
                if (profile.StartupNotes is { Count: > 0 })
                {
                    foreach (var note in profile.StartupNotes)
                        AppendLog(note);
                }
                else
                {
                    AppendLog("Closing this window does not stop the service.");
                    if (AdminUtil.IsAdministrator())
                        AppendLog("Running elevated: Start/Stop/Restart can control the Windows service. Install Windows service runs in this token (no second UAC). Closing this window does not stop the service. Stop the service before reinstall if Python still holds pywin32.");
                    else
                        AppendLog("Not elevated: status, PID, and health still work. From Master Launch Control use Restart as administrator once, leave it open, then Open Launch Control from the card so this window inherits that token. Install Windows service will prompt for UAC if you stay unelevated.");
                }
                SeekLogsToEnd();
                await RefreshStatusAsync();
                if (_profile.ShowVenvUi)
                    await RefreshVenvLockersAsync(logEach: false);
            }
            catch (Exception ex)
            {
                if (HealthClient.IsExpectedDisconnect(ex))
                    LcHostLog.Warn("Health probe connection closed (service stopping or restarting): " + ex.Message);
                else
                    LcHostLog.Error("Launch Control loaded with an error", ex);
            }
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _timer.Tick += async (_, _) =>
        {
            if (Interlocked.Exchange(ref _statusGate, 1) != 0)
                return;
            try
            {
                PumpLogs();
                await RefreshStatusAsync();
                if (_profile.ShowVenvUi)
                    await RefreshVenvLockersAsync(logEach: false);
            }
            catch (Exception ex)
            {
                if (HealthClient.IsExpectedDisconnect(ex))
                    LcHostLog.Warn("Health probe connection closed (service stopping or restarting): " + ex.Message);
                else
                    AppendLog(ex.Message, "WARN");
            }
            finally
            {
                Interlocked.Exchange(ref _statusGate, 0);
            }
        };
        _timer.Start();

        Closed += (_, _) =>
        {
            _timer.Stop();
            _themeWindow?.Close();
            // Do not stop the Windows service or the child if it was started as a service.
            // Session process is left running (same as PS LCs).
        };
    }

    private void ApplyHostChrome()
    {
        if (!string.IsNullOrWhiteSpace(_profile.FooterText))
            FooterText.Text = _profile.FooterText;
        if (!string.IsNullOrWhiteSpace(_profile.StartButtonText))
            StartButton.Content = _profile.StartButtonText;
        if (!string.IsNullOrWhiteSpace(_profile.StopButtonText))
            StopButton.Content = _profile.StopButtonText;

        var useCompactServices = _profile.CompactServiceControls
            && _profile.ServiceNames is { Length: > 0 }
            && _profile.ShowStartStopButtons;
        if (useCompactServices)
        {
            ClassicServiceButtons.Visibility = Visibility.Collapsed;
            CompactServicesHost.Visibility = Visibility.Visible;
            BuildCompactServiceControls();
        }
        else
        {
            var canOperate = _profile.ShowStartStopButtons
                && ((_profile.ServiceNames is { Length: > 0 }) || _profile.ProcessFallback is not null);
            var startStop = canOperate ? Visibility.Visible : Visibility.Collapsed;
            ClassicServiceButtons.Visibility = startStop;
            StartButton.Visibility = startStop;
            StopButton.Visibility = startStop;
            RestartButton.Visibility = canOperate && _profile.ShowRestartButton
                ? Visibility.Visible
                : Visibility.Collapsed;
            CompactServicesHost.Visibility = Visibility.Collapsed;
        }

        BrowserButton.Visibility = _profile.ShowBrowserButton
            && !string.IsNullOrWhiteSpace(_profile.BrowserUrl)
            ? Visibility.Visible
            : Visibility.Collapsed;
        FollowLogsButton.Visibility = _profile.ShowFollowLogsButton && _profile.LogPaths is { Length: > 0 }
            ? Visibility.Visible
            : Visibility.Collapsed;
        RefreshButton.Visibility = _profile.ShowRefreshButton
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!_profile.ShowVenvUi)
        {
            VenvLockHeader.Visibility = Visibility.Collapsed;
            VenvLockBox.Visibility = Visibility.Collapsed;
            ShowVenvButton.Visibility = Visibility.Collapsed;
        }
    }

    public void SetFollowLogs(bool enabled)
    {
        _followLogs = enabled;
        FollowLogsButton.Content = enabled ? "Follow logs: ON" : "Follow logs: OFF";
        if (enabled) SeekLogsToEnd();
    }

    public void ToggleFollowLogs()
    {
        SetFollowLogs(!_followLogs);
        AppendLog(_followLogs ? "Follow logs ON" : "Follow logs OFF — status changes and WARN/ERROR only");
    }

    public async Task RequestStatusRefreshAsync()
    {
        PumpLogs();
        await RefreshStatusAsync();
        if (_profile.ShowVenvUi)
            await RefreshVenvLockersAsync(logEach: false);
        AppendLog("Status refresh requested");
    }

    private readonly Dictionary<string, TextBlock> _compactServiceStatus = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _compactServiceStart = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _compactServiceStop = new(StringComparer.OrdinalIgnoreCase);

    private void BuildCompactServiceControls()
    {
        CompactServicesHost.Children.Clear();
        _compactServiceStatus.Clear();
        _compactServiceStart.Clear();
        _compactServiceStop.Clear();

        var box = new GroupBox
        {
            Header = "Windows services",
            Margin = new Thickness(0, 4, 0, 8),
            Padding = new Thickness(8, 6, 8, 8),
            Foreground = (Brush)FindResource("MetaTextBrush")
        };
        var stack = new StackPanel();
        foreach (var name in _profile.ServiceNames)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
            var title = new TextBlock
            {
                Text = FriendlyServiceTitle(name),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("BodyTextBrush")
            };
            var status = new TextBlock
            {
                Text = "…",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("StatusUnknownBrush")
            };
            DockPanel.SetDock(status, Dock.Right);
            header.Children.Add(status);
            header.Children.Add(title);
            row.Children.Add(header);

            var buttons = new UniformGrid { Columns = 3 };
            var start = MakeCompactServiceButton("Start");
            var stop = MakeCompactServiceButton("Stop");
            var restart = MakeCompactServiceButton("Restart");
            var svcName = name;
            start.Click += async (_, _) => await ControlNamedServiceAsync(svcName, "Start");
            stop.Click += async (_, _) => await ControlNamedServiceAsync(svcName, "Stop");
            restart.Click += async (_, _) => await ControlNamedServiceAsync(svcName, "Restart");
            buttons.Children.Add(start);
            buttons.Children.Add(stop);
            buttons.Children.Add(restart);
            row.Children.Add(buttons);
            stack.Children.Add(row);

            _compactServiceStatus[name] = status;
            _compactServiceStart[name] = start;
            _compactServiceStop[name] = stop;
        }
        box.Content = stack;
        CompactServicesHost.Children.Add(box);
    }

    private static string FriendlyServiceTitle(string serviceName) =>
        serviceName switch
        {
            "HeimdallApi" => "Heimdall API",
            "HeimdallAgent" => "Heimdall Agent",
            _ => serviceName
        };

    private Button MakeCompactServiceButton(string text) =>
        new()
        {
            Content = text,
            Margin = new Thickness(0, 0, 4, 0),
            Padding = new Thickness(6, 2, 6, 2),
            MinHeight = 24,
            Style = (Style)FindResource("SecondaryButtonStyle")
        };

    private async Task ControlNamedServiceAsync(string serviceName, string action)
    {
        if (!AdminUtil.IsAdministrator())
        {
            AppendLog("Service control refused: not elevated. " + AdminUtil.RestartAsAdminHint, "ERROR");
            return;
        }
        try
        {
            AppendLog($"{action} Windows service {serviceName}", "STEP");
            var root = ResolveInstallRoot();
            if (string.Equals(action, "Restart", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Run(() => ServiceRuntime.Stop(serviceName, root));
                await Task.Run(() => ServiceRuntime.Start(serviceName));
            }
            else if (string.Equals(action, "Start", StringComparison.OrdinalIgnoreCase))
                await Task.Run(() => ServiceRuntime.Start(serviceName));
            else
                await Task.Run(() => ServiceRuntime.Stop(serviceName, root));
            await RefreshStatusAsync();
            AppendLog($"{action} {serviceName} requested.", "OK");
        }
        catch (Exception ex)
        {
            AppendLog(ServiceRuntime.FormatException(ex), "ERROR");
        }
    }

    private void BuildExtraButtons()
    {
        ExtraButtons.Items.Clear();
        var layout = _profile.ExtraActionLayout;
        var collapsed = new HashSet<string>(layout?.CollapsedGroups ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var compact = new HashSet<string>(layout?.CompactGroups ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        string? lastGroup = "\0";
        List<ExtraAction>? compactBatch = null;
        string? compactGroupName = null;

        void FlushCompact()
        {
            if (compactBatch is null || compactGroupName is null || compactBatch.Count == 0)
                return;
            ExtraButtons.Items.Add(BuildCompactActionGroup(compactGroupName, compactBatch));
            compactBatch = null;
            compactGroupName = null;
        }

        foreach (var action in StandardAndProfileActions())
        {
            var group = action.Group ?? "";
            if (!string.Equals(group, lastGroup, StringComparison.Ordinal))
            {
                FlushCompact();
                lastGroup = group;
            }

            if (!string.IsNullOrWhiteSpace(group) && compact.Contains(group))
            {
                compactBatch ??= new List<ExtraAction>();
                compactGroupName = group;
                compactBatch.Add(action);
                continue;
            }

            FlushCompact();
            WireGroupedAction(action, collapsed);
        }
        FlushCompact();
    }

    private void WireGroupedAction(ExtraAction action, HashSet<string> collapsed)
    {
        var group = action.Group;
        if (!string.IsNullOrWhiteSpace(group) && collapsed.Contains(group))
        {
            Expander? expander = null;
            StackPanel? body = null;
            foreach (var item in ExtraButtons.Items)
            {
                if (item is Expander ex && string.Equals(ex.Tag as string, group, StringComparison.OrdinalIgnoreCase))
                {
                    expander = ex;
                    body = ex.Content as StackPanel;
                    break;
                }
            }
            if (expander is null)
            {
                body = new StackPanel();
                expander = new Expander
                {
                    Header = group,
                    IsExpanded = false,
                    Margin = new Thickness(0, 10, 0, 4),
                    Tag = group,
                    Foreground = (Brush)FindResource("MetaTextBrush"),
                    Content = body
                };
                ExtraButtons.Items.Add(expander);
            }
            body!.Children.Add(MakeExtraActionButton(action));
            return;
        }

        if (!string.IsNullOrWhiteSpace(group))
        {
            // Walk past trailing buttons so consecutive actions in the same group
            // share one heading (previously every button got its own heading).
            var needHeading = true;
            for (var i = ExtraButtons.Items.Count - 1; i >= 0; i--)
            {
                var item = ExtraButtons.Items[i];
                if (item is Button)
                    continue;
                if (item is TextBlock tb && string.Equals(tb.Text, group, StringComparison.OrdinalIgnoreCase))
                    needHeading = false;
                else if (item is GroupBox gb && string.Equals(gb.Header as string, group, StringComparison.OrdinalIgnoreCase))
                    needHeading = false;
                else if (item is Expander ex && string.Equals(ex.Tag as string, group, StringComparison.OrdinalIgnoreCase))
                    needHeading = false;
                break;
            }
            if (needHeading)
            {
                ExtraButtons.Items.Add(new TextBlock
                {
                    Text = group,
                    Margin = new Thickness(0, 12, 0, 6),
                    Foreground = (Brush)FindResource("MetaTextBrush")
                });
            }
        }

        ExtraButtons.Items.Add(MakeExtraActionButton(action));
    }

    private UIElement BuildCompactActionGroup(string groupName, List<ExtraAction> actions)
    {
        var box = new GroupBox
        {
            Header = groupName,
            Margin = new Thickness(0, 10, 0, 6),
            Padding = new Thickness(8, 8, 8, 8),
            Foreground = (Brush)FindResource("MetaTextBrush")
        };
        var panel = new StackPanel();
        foreach (var action in actions)
            panel.Children.Add(MakeExtraActionButton(action, compact: true));
        box.Content = panel;
        return box;
    }

    private Button MakeExtraActionButton(ExtraAction action, bool compact = false)
    {
        var btn = new Button
        {
            Content = action.Title,
            Style = (Style)FindResource("SecondaryButtonStyle"),
            Margin = new Thickness(0, 0, 0, compact ? 4 : 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            Padding = compact ? new Thickness(8, 2, 8, 2) : new Thickness(10, 6, 10, 6),
            MinHeight = compact ? 26 : 32
        };
        var captured = action;
        btn.Click += async (_, _) =>
        {
            try
            {
                var isServiceInstall = captured.Title.Contains("Install Windows service", StringComparison.OrdinalIgnoreCase);
                if (isServiceInstall)
                {
                    if (!AdminUtil.IsAdministrator())
                    {
                        AppendLog("Not running as administrator. Windows will show a UAC prompt — approve it to install. Cancelling UAC leaves the service unchanged. Prefer: elevate Master Launch Control once, then Open Launch Control from the card so this window inherits that token.");
                    }
                    if (!await PrepareForServiceInstallAsync())
                        return;
                }
                captured.Run(this);
            }
            catch (Exception ex)
            {
                AppendLog(ex.Message, "ERROR");
                LcHostLog.Error($"Extra action '{captured.Title}' failed", ex);
            }
        };
        return btn;
    }

    private IEnumerable<ExtraAction> StandardAndProfileActions()
    {
        var existing = _profile.ExtraActions ?? [];
        var titles = existing.Select(a => a.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var injected = new List<ExtraAction>();

        var logDirs = _profile.LogPaths
            .Select(p => Directory.Exists(p) ? p : Path.GetDirectoryName(p))
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var hasOpenLogs = titles.Any(t =>
            t.Contains("Open logs", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Open data folder", StringComparison.OrdinalIgnoreCase));
        if (!hasOpenLogs && logDirs.Count > 0)
        {
            var first = logDirs[0];
            injected.Add(new ExtraAction("Open logs folder", _ => ProcessUtil.OpenPath(first), "Logs"));
        }

        if (!string.IsNullOrWhiteSpace(_profile.DiagnosticsFlagPath)
            && !titles.Any(t => t.Contains("Diagnostics ON", StringComparison.OrdinalIgnoreCase)))
        {
            var flag = _profile.DiagnosticsFlagPath!;
            injected.Add(new ExtraAction("Diagnostics ON", w =>
            {
                var dir = Path.GetDirectoryName(flag);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(flag, DateTimeOffset.Now.ToString("o"));
                w.AppendLog("Diagnostics ON");
            }, "Diagnostics"));
            injected.Add(new ExtraAction("Diagnostics OFF", w =>
            {
                if (File.Exists(flag)) File.Delete(flag);
                w.AppendLog("Diagnostics OFF");
            }, "Diagnostics"));
        }

        // Profile actions first so product grouping/order wins; inject only gaps.
        return existing.Concat(injected);
    }

    public void BeginElevatedPowerShell(string scriptPath, string workingDirectory, string? logPath = null, params string[] extraArgs)
    {
        _ = ObserveAsync(RunElevatedPowerShellAsync(scriptPath, workingDirectory, logPath, extraArgs));
    }

    private async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (HealthClient.IsExpectedDisconnect(ex))
                LcHostLog.Warn("Health probe connection closed (service stopping or restarting): " + ex.Message);
            else
            {
                AppendLog(ex.Message, "ERROR");
                LcHostLog.Error("Background Launch Control task failed", ex);
            }
        }
    }

    private async Task RunElevatedPowerShellAsync(
        string scriptPath,
        string workingDirectory,
        string? logPath,
        string[] extraArgs)
    {
        if (!File.Exists(scriptPath))
        {
            AppendLog($"Missing {scriptPath}", "ERROR");
            return;
        }

        var leaf = Path.GetFileName(scriptPath);
        if (AdminUtil.IsAdministrator())
        {
            AppendLog(string.IsNullOrWhiteSpace(logPath)
                ? $"Already elevated — running {leaf} in this token (no UAC spawn)."
                : $"Already elevated — running {leaf} in this token (no UAC spawn). Log: {logPath}");
        }
        else
        {
            AppendLog(string.IsNullOrWhiteSpace(logPath)
                ? $"Launching {leaf} with UAC elevation (waiting)."
                : $"Launching {leaf} with UAC elevation (waiting). Log: {logPath}");
        }

        long offset = 0;
        if (!string.IsNullOrWhiteSpace(logPath) && File.Exists(logPath))
        {
            try { offset = new FileInfo(logPath).Length; }
            catch { offset = 0; }
        }

        int exit;
        try
        {
            exit = await Task.Run(() =>
                ProcessUtil.StartElevatedPowerShellWait(scriptPath, workingDirectory, extraArgs, logPath)).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            AppendLog(ex.Message, "ERROR");
            return;
        }
        catch (Exception ex)
        {
            AppendLog($"Elevation failed: {ex.Message}", "ERROR");
            return;
        }

        AppendLog($"Elevated installer exit code: {exit}");
        if (exit != 0 && !string.IsNullOrWhiteSpace(logPath))
            AppendLog($"Installer reported failure. See {logPath} (elevated window stays open on error).", "ERROR");
        else if (exit != 0)
            AppendLog("Installer reported failure.", "ERROR");
        else if (leaf.Contains("install", StringComparison.OrdinalIgnoreCase))
        {
            var svc = _profile.ServiceNames.Select(ServiceRuntime.Query).FirstOrDefault(i => i.Exists);
            if (svc is null)
                AppendLog("Installer exit 0 but the Windows service is not registered. See the install log.", "ERROR");
            else if (!svc.State.Contains("Running", StringComparison.OrdinalIgnoreCase))
                AppendLog($"Installer exit 0 but service {svc.Name} is {svc.State} (not Running). See {logPath ?? "the install log"} and Event Viewer → Application.", "ERROR");
            else
                AppendLog($"Service {svc.Name} is Running.");
        }

        if (!string.IsNullOrWhiteSpace(logPath))
        {
            var lines = LogTailer.ReadNewLines(logPath, ref offset);
            if (lines.Count > 0)
            {
                AppendLog($"--- installer log ({logPath}) ---");
                foreach (var line in lines)
                    AppendLog(line);
                AppendLog("--- end installer log ---");
            }
            else if (!File.Exists(logPath))
                AppendLog($"No installer log at {logPath}. Elevated PowerShell may have failed before the script ran.", "WARN");
            else
                AppendLog($"Installer log unchanged at {logPath}", "WARN");
        }

        try
        {
            await RefreshStatusAsync();
            if (_profile.ShowVenvUi)
                await RefreshVenvLockersAsync(logEach: false);
        }
        catch (Exception ex)
        {
            if (HealthClient.IsExpectedDisconnect(ex))
                LcHostLog.Warn("Health probe connection closed (service stopping or restarting): " + ex.Message);
            else
                AppendLog(ex.Message, "WARN");
        }
    }

    public void AppendLog(string message, string level = "INFO")
    {
        AppendLogPane(message, level);
        LcHostLog.WriteFile(level, message);
    }

    public void AppendLogPane(string message, string level = "INFO")
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AppendLogPane(message, level));
            return;
        }
        AppendColoredLogLine(line, level);
        TrimLog();
        AutoScrollLogIfPinned();
    }

    private void AppendColoredLogLine(string line, string level)
    {
        // Red = failures only. Blue = important/steps. Amber = warnings. Green = success. Default = body.
        var brush = level.ToUpperInvariant() switch
        {
            "OK" or "SUCCESS" => (Brush)FindResource("StatusRunningBrush"),
            "WARN" or "WARNING" or "ASK" => (Brush)FindResource("StrongWarmBrush"),
            "ERROR" or "FAIL" or "FAILED" => (Brush)FindResource("ErrorBrush"),
            "STEP" or "IMPORTANT" => (Brush)FindResource("ConsoleImportantBrush"),
            _ => (Brush)FindResource("ConsoleTextBrush")
        };
        var run = new Run(line + Environment.NewLine) { Foreground = brush };
        if (LogBox.Document.Blocks.LastBlock is Paragraph p)
            p.Inlines.Add(run);
        else
            LogBox.Document.Blocks.Add(new Paragraph(run) { Margin = new Thickness(0) });
    }

    private static string InferLevelFromLine(string line)
    {
        if (line.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) || line.Contains("[FAIL", StringComparison.OrdinalIgnoreCase))
            return "ERROR";
        if (line.Contains("[WARN", StringComparison.OrdinalIgnoreCase))
            return "WARN";
        if (line.Contains("[OK]", StringComparison.OrdinalIgnoreCase) || line.Contains("[SUCCESS]", StringComparison.OrdinalIgnoreCase))
            return "OK";
        if (line.Contains("[STEP]", StringComparison.OrdinalIgnoreCase) || line.Contains("[IMPORTANT]", StringComparison.OrdinalIgnoreCase))
            return "STEP";
        return "INFO";
    }

    private void TrimLog()
    {
        var textRange = new TextRange(LogBox.Document.ContentStart, LogBox.Document.ContentEnd);
        var full = textRange.Text;
        if (full.Length <= 80000) return;
        var keep = full[^60000..];
        LogBox.Document.Blocks.Clear();
        LogBox.Document.Blocks.Add(new Paragraph(new Run(keep) { Foreground = (Brush)FindResource("ConsoleTextBrush") })
        {
            Margin = new Thickness(0)
        });
    }

    private void SeekLogsToEnd()
    {
        foreach (var path in _profile.LogPaths)
        {
            try
            {
                if (File.Exists(path))
                    _logOffsets[path] = new FileInfo(path).Length;
                else
                    _logOffsets[path] = 0;
            }
            catch { _logOffsets[path] = 0; }
        }
        _logReady = true;
    }

    private void PumpLogs()
    {
        foreach (var path in _profile.LogPaths)
        {
            _logOffsets.TryGetValue(path, out var offset);
            var lines = LogTailer.ReadNewLines(path, ref offset);
            _logOffsets[path] = offset;
            if (!_logReady) continue;
            foreach (var line in lines)
            {
                if (_followLogs || LogTailer.IsImportant(line))
                    AppendColoredLogLine(line, InferLevelFromLine(line));
            }
        }
        TrimLog();
        AutoScrollLogIfPinned();
    }

    /// <summary>
    /// Only jump to end when Follow is ON and the user is already near the bottom.
    /// Scrolling up preserves position while new lines keep appending.
    /// </summary>
    private void AutoScrollLogIfPinned()
    {
        if (!_followLogs) return;
        var sv = FindVisualChild<ScrollViewer>(LogBox);
        if (sv is null)
        {
            LogBox.ScrollToEnd();
            return;
        }
        const double thresholdPx = 48;
        if (sv.ScrollableHeight <= 0 || (sv.ScrollableHeight - sv.VerticalOffset) <= thresholdPx)
            LogBox.ScrollToEnd();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var nested = FindVisualChild<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            var infos = _profile.ServiceNames.Select(ServiceRuntime.Query).ToList();
            var anyService = infos.Any(i => i.Exists);
            var running = infos.Any(i => i.Exists && i.State.Contains("Running", StringComparison.OrdinalIgnoreCase));
            var starting = infos.Any(i => i.State.Contains("Start", StringComparison.OrdinalIgnoreCase) && i.State.Contains("Pending", StringComparison.OrdinalIgnoreCase));
            var stopping = infos.Any(i => i.State.Contains("Stop", StringComparison.OrdinalIgnoreCase) && i.State.Contains("Pending", StringComparison.OrdinalIgnoreCase));

            var childAlive = _child is { HasExited: false };
            var pid = 0;
            if (childAlive) pid = _child!.Id;
            if (pid <= 0)
            {
                try
                {
                    var tracked = _profile.ProcessFallback?.FindRunningPids?.Invoke();
                    if (tracked is { Count: > 0 })
                    {
                        pid = tracked[0];
                        childAlive = true;
                    }
                }
                catch { }
            }
            // Windows service mode: ProcessId comes from Win32_Service (ServiceController has none).
            if (pid <= 0)
            {
                pid = infos
                    .Where(i => i.Exists
                                && i.ProcessId > 0
                                && i.State.Contains("Running", StringComparison.OrdinalIgnoreCase))
                    .Select(i => i.ProcessId)
                    .FirstOrDefault();
            }

            HealthClient.Result health = new(false, 0, "not checked");
            if (!string.IsNullOrWhiteSpace(_profile.HealthUrl))
                health = await HealthClient.GetAsync(_profile.HealthUrl);

            string status;
            Brush brush;
            if (starting) { status = "Starting"; brush = (Brush)FindResource("StatusStartingBrush"); }
            else if (stopping) { status = "Stopping"; brush = (Brush)FindResource("StatusStoppingBrush"); }
            else if (running || health.Ok || childAlive)
            {
                status = health.Ok || running ? "Running" : "Unreachable";
                brush = health.Ok || running
                    ? (Brush)FindResource("StatusRunningBrush")
                    : (Brush)FindResource("StatusUnreachableBrush");
            }
            else if (anyService) { status = "Stopped"; brush = (Brush)FindResource("StatusStoppedBrush"); }
            else { status = "Stopped"; brush = (Brush)FindResource("StatusStoppedBrush"); }

            StatusLabel.Text = status;
            StatusLabel.Foreground = brush;
            PidLabel.Text = pid > 0 ? $"PID {pid}" : "PID —";

            ServiceLine.Text = infos.Count == 0
                ? "Service: —"
                : string.Join("  ·  ", infos.Select(i =>
                    i.Exists ? $"{i.Name}: {i.State}" : $"{i.Name}: not installed"));

            foreach (var info in infos)
            {
                if (!_compactServiceStatus.TryGetValue(info.Name, out var lbl)) continue;
                var runningSvc = info.Exists && info.State.Contains("Running", StringComparison.OrdinalIgnoreCase);
                lbl.Text = info.Exists ? info.State : "not installed";
                lbl.Foreground = runningSvc
                    ? (Brush)FindResource("StatusRunningBrush")
                    : info.Exists
                        ? (Brush)FindResource("StatusStoppedBrush")
                        : (Brush)FindResource("StatusUnknownBrush");
                if (_compactServiceStart.TryGetValue(info.Name, out var startBtn))
                    startBtn.IsEnabled = info.Exists && !runningSvc;
                if (_compactServiceStop.TryGetValue(info.Name, out var stopBtn))
                    stopBtn.IsEnabled = info.Exists && runningSvc;
            }

            HealthLine.Text = FormatHealthLine(health);
            HealthLine.Foreground = health.Ok
                ? (Brush)FindResource("StatusRunningBrush")
                : (Brush)FindResource("SoftAccentBrush");

            ModeLine.Text = anyService
                ? "Mode: Windows service"
                : "Mode: no Windows service; attached/local process";

            MetaText.Text = _profile.MetaText?.Invoke()
                            ?? $"{(_profile.BrowserUrl ?? _profile.HealthUrl ?? "")}";

            var needAdmin = anyService && !AdminUtil.IsAdministrator();
            if (needAdmin)
            {
                AdminLabel.Text = "Not elevated. Start/Stop/Restart will refuse until this window is administrator. From Master Launch Control: Restart as administrator once, then Open Launch Control from the card.";
                AdminLabel.Visibility = Visibility.Visible;
            }
            else if (!anyService)
            {
                if (_profile.ShowVenvUi)
                {
                    AdminLabel.Text = AdminUtil.IsAdministrator()
                        ? "Service not installed. Use Install Windows service (runs in this token; no second UAC). Stop leftover Python in this venv before pip/pywin32."
                        : "Service not installed. Use Install Windows service (UAC) for reboot-safe start, or elevate Master Launch Control first so Open Launch Control inherits admin.";
                    AdminLabel.Visibility = Visibility.Visible;
                }
                else
                {
                    AdminLabel.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                AdminLabel.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            AppendLog(ex.Message, "ERROR");
        }
    }

    private static string FormatHealthLine(HealthClient.Result health)
    {
        if (!health.Ok)
            return $"Health: FAIL ({health.Error})";

        var line = $"Health: OK {health.Ms}ms";
        if (!string.IsNullOrWhiteSpace(health.ProductVersion))
        {
            var shortVer = health.ProductVersion.Split('+', 2)[0].Trim();
            if (!string.IsNullOrWhiteSpace(shortVer))
                line += $" · API {shortVer}";
        }

        return line;
    }

    private async void Start_Click(object sender, RoutedEventArgs e) => await RunOp(StartCoreAsync);
    private async void Stop_Click(object sender, RoutedEventArgs e) => await RunOp(StopCoreAsync);
    private async void Restart_Click(object sender, RoutedEventArgs e) => await RunOp(RestartCoreAsync);
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        PumpLogs();
        await RefreshStatusAsync();
        if (_profile.ShowVenvUi)
            await RefreshVenvLockersAsync(logEach: false);
        AppendLog("Status refresh requested");
    }

    private async void ShowVenv_Click(object sender, RoutedEventArgs e)
    {
        if (!_profile.ShowVenvUi)
            return;
        await RefreshVenvLockersAsync(logEach: true);
    }

    private void FollowLogs_Click(object sender, RoutedEventArgs e)
    {
        _followLogs = !_followLogs;
        FollowLogsButton.Content = _followLogs ? "Follow logs: ON" : "Follow logs: OFF";
        AppendLog(_followLogs ? "Follow logs ON" : "Follow logs OFF — status changes and WARN/ERROR only");
        if (_followLogs) SeekLogsToEnd();
    }

    private void Browser_Click(object sender, RoutedEventArgs e)
    {
        var url = _profile.BrowserUrl ?? _profile.HealthUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            AppendLog("No browser URL configured.", "WARN");
            return;
        }
        ProcessUtil.OpenUrl(url);
        AppendLog($"Opened {url}");
    }

    private async Task RunOp(Func<Task> op)
    {
        if (_busy) return;
        _busy = true;
        try { await op(); }
        catch (Exception ex) { AppendLog(ServiceRuntime.FormatException(ex), "ERROR"); }
        finally
        {
            _busy = false;
            await RefreshStatusAsync();
            if (_profile.ShowVenvUi)
                await RefreshVenvLockersAsync(logEach: false);
        }
    }

    private string? ResolveInstallRoot()
    {
        if (!string.IsNullOrWhiteSpace(_profile.InstallRoot))
            return _profile.InstallRoot;
        try
        {
            return _profile.ProcessFallback?.WorkingDirectory();
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> PrepareForServiceInstallAsync()
    {
        AppendLog("Other products (Heimdall, Switcheroo, Serraview Insights) use separate Python venvs. Heimdall/Switcheroo running is normal and does not lock this product's pywin32 DLL. The blocker is this install's python.exe / pythonw.exe / pythonservice.exe.");
        await RefreshVenvLockersAsync(logEach: true);

        var installed = _profile.ServiceNames.Select(ServiceRuntime.Query).Where(i => i.Exists).ToList();
        if (installed.Count > 0 && AdminUtil.IsAdministrator())
        {
            var running = installed.Where(i =>
                !i.State.Contains("Stopped", StringComparison.OrdinalIgnoreCase)).ToList();
            if (running.Count > 0)
            {
                AppendLog("Service is not Stopped. Install/reinstall must stop it and leftover Python before pip so this venv's pywintypes DLL can be overwritten.");
                foreach (var svc in running.AsEnumerable().Reverse())
                {
                    AppendLog($"Stopping Windows service {svc.Name} before install pip.");
                    try
                    {
                        await Task.Run(() => ServiceRuntime.Stop(svc.Name, ResolveInstallRoot()));
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"Stop {svc.Name} failed: {ServiceRuntime.FormatException(ex)}. Will still stop leftover Python in this venv.", "WARN");
                    }
                }
            }

            if (!await StopVenvLockersAsync("before install pip"))
                return false;
        }
        else if (installed.Count > 0)
        {
            AppendLog("This window is not elevated; the UAC installer will stop the service and leftover Python before pip.");
        }

        var remaining = await CurrentVenvLockersAsync();
        ApplyVenvResult(remaining, logEach: true);
        if (remaining.Lockers.Count > 0)
        {
            var pids = string.Join(", ", remaining.Lockers.Select(l => l.Pid));
            if (AdminUtil.IsAdministrator())
            {
                AppendLog($"Python still using this venv (PID {pids}). Stop until this list is empty, then click Install. Pip is refused while those processes hold pywintypes.", "ERROR");
                return false;
            }

            AppendLog($"Python still using this venv (PID {pids}). The elevated installer will stop those PIDs before pip.", "WARN");
        }

        return true;
    }

    private async Task<bool> StopVenvLockersAsync(string reason)
    {
        var root = ResolveInstallRoot();
        if (string.IsNullOrWhiteSpace(root))
            return true;

        AppendLog($"Waiting for leftover python.exe / pythonw.exe / pythonservice.exe in this venv to exit ({reason}).");
        var result = await Task.Run(() =>
            VenvProcessProbe.StopRemaining(root, _profile.ServiceNames, TimeSpan.FromSeconds(20)))
            .ConfigureAwait(true);
        ApplyVenvResult(result, logEach: true);
        if (result.Lockers.Count == 0)
            return true;

        var pids = string.Join(", ", result.Lockers.Select(l => l.Pid));
        AppendLog($"Could not clear this venv. Still running: PID {pids}. Stop those, then retry.", "ERROR");
        return false;
    }

    private async Task<VenvProbeResult> CurrentVenvLockersAsync()
    {
        var root = ResolveInstallRoot();
        if (string.IsNullOrWhiteSpace(root))
            return new([], "Install folder is not set.");
        return await Task.Run(() => VenvProcessProbe.Find(root, _profile.ServiceNames)).ConfigureAwait(true);
    }

    private async Task RefreshVenvLockersAsync(bool logEach)
    {
        if (!_profile.ShowVenvUi)
        {
            VenvLockHeader.Visibility = Visibility.Collapsed;
            VenvLockBox.Visibility = Visibility.Collapsed;
            ShowVenvButton.Visibility = Visibility.Collapsed;
            return;
        }
        var root = ResolveInstallRoot();
        var show = !string.IsNullOrWhiteSpace(root);
        VenvLockHeader.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        VenvLockBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ShowVenvButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
            return;

        if (logEach)
            VenvLockBox.Text = "Querying Win32_Process…";
        var result = await CurrentVenvLockersAsync();
        ApplyVenvResult(result, logEach);
    }

    private void ApplyVenvResult(VenvProbeResult result, bool logEach)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ApplyVenvResult(result, logEach));
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            VenvLockBox.Text = result.Error;
            if (logEach)
                AppendLog(result.Error, "WARN");
            return;
        }

        if (result.Lockers.Count == 0)
        {
            VenvLockBox.Text = "(none — this venv is free)";
            if (logEach)
                AppendLog("No python.exe / pythonw.exe / pythonservice.exe is using this install's .venv, venv, or python -m app.");
            return;
        }

        VenvLockBox.Text = string.Join(Environment.NewLine, result.Lockers.Select(VenvProcessProbe.FormatLine));
        if (logEach)
        {
            AppendLog($"Python processes using this install ({result.Lockers.Count}):");
            foreach (var locker in result.Lockers)
                AppendLog(VenvProcessProbe.FormatLine(locker), "WARN");
        }
    }

    private async Task StartCoreAsync()
    {
        if (!string.IsNullOrWhiteSpace(_profile.HealthUrl))
        {
            var health = await HealthClient.GetAsync(_profile.HealthUrl);
            if (health.Ok)
            {
                AppendLog("Already running (health ok). Refusing a second start.");
                return;
            }
        }

        var installed = _profile.ServiceNames.Select(ServiceRuntime.Query).Where(i => i.Exists).ToList();
        if (installed.Count > 0)
        {
            if (!AdminUtil.IsAdministrator())
            {
                const string msg =
                    "This Launch Control is not running as administrator, so Start/Stop/Restart cannot control the Windows service.\n\n" +
                    "From Master Launch Control: Restart as administrator once, leave it open, then Open Launch Control from the card (this window inherits that token).";
                AppendLog("Start refused: not elevated. " + AdminUtil.RestartAsAdminHint, "ERROR");
                MessageBox.Show(this, msg, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            foreach (var svc in installed)
            {
                var live = ServiceRuntime.Query(svc.Name);
                var hung = live.Exists
                    && !live.State.Equals("Stopped", StringComparison.OrdinalIgnoreCase)
                    && !live.State.Equals("StartPending", StringComparison.OrdinalIgnoreCase);
                if (hung)
                {
                    AppendLog($"Service {svc.Name} is {live.State} (PID {live.ProcessId}) but health failed — treating as hung wrapper; forcing stop then start.");
                    await Task.Run(() => ServiceRuntime.Stop(svc.Name, ResolveInstallRoot()));
                }
                AppendLog($"Starting Windows service {svc.Name}");
                await Task.Run(() => ServiceRuntime.Start(svc.Name));
            }
            _startedAt = DateTime.Now;
            return;
        }

        if (_profile.ProcessFallback is null)
        {
            AppendLog("No Windows service installed and no process fallback.", "ERROR");
            return;
        }

        StartFallbackProcess();
    }

    private async Task StopCoreAsync()
    {
        var installed = _profile.ServiceNames.Select(ServiceRuntime.Query).Where(i => i.Exists).ToList();
        if (installed.Count > 0)
        {
            if (!AdminUtil.IsAdministrator())
            {
                const string msg =
                    "This Launch Control is not running as administrator, so Start/Stop/Restart cannot control the Windows service.\n\n" +
                    "From Master Launch Control: Restart as administrator once, leave it open, then Open Launch Control from the card (this window inherits that token).";
                AppendLog("Stop refused: not elevated. " + AdminUtil.RestartAsAdminHint, "ERROR");
                MessageBox.Show(this, msg, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            foreach (var svc in installed.AsEnumerable().Reverse())
            {
                AppendLog($"Stopping Windows service {svc.Name}");
                await Task.Run(() => ServiceRuntime.Stop(svc.Name, ResolveInstallRoot()));
            }
            await StopVenvLockersAsync("after Stop");
            _startedAt = null;
            return;
        }

        StopFallbackProcess();
        if (_profile.ShowVenvUi)
            await StopVenvLockersAsync("after Stop");
    }

    private async Task RestartCoreAsync()
    {
        await StopCoreAsync();
        await Task.Delay(400);
        await StartCoreAsync();
    }

    private void StartFallbackProcess()
    {
        var spec = _profile.ProcessFallback!;
        var work = spec.WorkingDirectory();
        var (file, args) = spec.StartInfo();
        if (!File.Exists(file))
        {
            AppendLog($"Cannot launch; missing {file}", "ERROR");
            return;
        }

        if (spec.DetachedGui)
        {
            var exeDir = Path.GetDirectoryName(Path.GetFullPath(file)) ?? "";
            if (string.IsNullOrWhiteSpace(work) || !Directory.Exists(work))
                work = exeDir;
            if (spec.LaunchNotes is not null)
            {
                foreach (var (message, level) in spec.LaunchNotes())
                    AppendLog(message, string.IsNullOrWhiteSpace(level) ? "INFO" : level);
            }
            AppendLog($"Launching {file} (cwd {work})");
            // UseShellExecute=false so WorkingDirectory is actually applied (shell start
            // often inherits the parent cwd — Explorer sets cwd to the exe folder).
            var started = Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = args ?? "",
                WorkingDirectory = work,
                UseShellExecute = false
            });
            if (started is not null)
                _child = started;
            _startedAt = DateTime.Now;
            AppendLog(started is null ? $"Started {file}" : $"Launched PID {started.Id}: {file}");
            return;
        }

        AppendLog($"Starting {file} {args} (session process; install the Windows service for reboot-safe start)");
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = work,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.Environment["PYTHONUNBUFFERED"] = "1";
        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        DataReceivedEventHandler handler = (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (_followLogs || LogTailer.IsImportant(e.Data))
                    AppendLog(e.Data);
            });
        };
        proc.OutputDataReceived += handler;
        proc.ErrorDataReceived += handler;
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        _child = proc;
        _startedAt = DateTime.Now;
        if (!string.IsNullOrWhiteSpace(spec.PidFile))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(spec.PidFile)!);
            File.WriteAllText(spec.PidFile, proc.Id.ToString());
        }
        AppendLog($"Process started PID {proc.Id}");
    }

    private void StopFallbackProcess()
    {
        var killed = new List<int>();
        if (_child is { HasExited: false })
        {
            var pid = _child.Id;
            AppendLog($"Stopping PID {pid}");
            try { _child.CloseMainWindow(); } catch { }
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = $"/PID {pid} /T",
                    UseShellExecute = false,
                    CreateNoWindow = true
                })?.WaitForExit(5000);
            }
            catch { }
            try { if (!_child.HasExited) _child.Kill(true); } catch { }
            killed.Add(pid);
        }
        _child = null;
        _startedAt = null;

        try
        {
            var extra = _profile.ProcessFallback?.FindRunningPids?.Invoke();
            if (extra is { Count: > 0 })
            {
                foreach (var pid in extra)
                {
                    if (killed.Contains(pid)) continue;
                    try
                    {
                        AppendLog($"Stopping PID {pid}");
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "taskkill.exe",
                            Arguments = $"/PID {pid} /T",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        })?.WaitForExit(5000);
                        killed.Add(pid);
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"Stop PID {pid} failed: {ex.Message}", "WARN");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog(ex.Message, "WARN");
        }

        if (!string.IsNullOrWhiteSpace(_profile.ProcessFallback?.PidFile) && File.Exists(_profile.ProcessFallback.PidFile))
        {
            try { File.Delete(_profile.ProcessFallback.PidFile); } catch { }
        }
        AppendLog(killed.Count == 0 ? "No process to stop." : $"Stopped PID(s) {string.Join(", ", killed)}");
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        LcHostLog.Info("Theme… clicked");
        AppendLog("Theme… clicked — Colors / Font / Going glass / Slots. Going glass is the Glass tab.");
        try
        {
            if (_themeWindow is { IsLoaded: true })
            {
                LcHostLog.Info("Theme window already open; activating");
                _themeWindow.Activate();
                return;
            }

            var current = ThemeService.Current;
            if (current is null)
            {
                LcHostLog.Error("Theme… clicked but ThemeService.Current is null");
                AppendLog("Theme service was not initialized. See " + LcHostLog.LogPath, "ERROR");
                MessageBox.Show(this,
                    "Theme service is not initialized.\n\nLog: " + LcHostLog.LogPath,
                    "Theme settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LcHostLog.Info("Constructing ThemeSettingsWindow");
            _themeWindow = new ThemeSettingsWindow(current, this, _profile.ProductName) { Owner = this };
            _themeWindow.Closed += (_, _) =>
            {
                LcHostLog.Info("Theme window closed");
                ClearThemeRoleHighlight();
                EndThemePickMode();
                _themeWindow = null;
            };
            BeginThemePickMode();
            _themeWindow.Show();
            LcHostLog.Info("ThemeSettingsWindow shown");
        }
        catch (Exception ex)
        {
            LcHostLog.Error("Theme window failed to open", ex);
            _themeWindow = null;
            EndThemePickMode();
            MessageBox.Show(this,
                "Theme window failed to open. Details were written to:\n" + LcHostLog.LogPath + "\n\n" + ex.Message,
                "Theme settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void SetThemeRoleHighlight(string? roleKey)
    {
        ClearThemeRoleHighlight();
        if (string.IsNullOrWhiteSpace(roleKey)) return;
        foreach (var element in FindElementsWithRole(this, roleKey))
        {
            var adorned = element is Window { Content: FrameworkElement content } ? content : element;
            var layer = AdornerLayer.GetAdornerLayer(adorned);
            if (layer is null) continue;
            var adorner = new ThemeRoleHighlightAdorner(adorned);
            layer.Add(adorner);
            _adorners.Add(adorner);
        }
    }

    public void ClearThemeRoleHighlight()
    {
        foreach (var adorner in _adorners)
        {
            if (AdornerLayer.GetAdornerLayer(adorner.AdornedElement) is { } layer)
                layer.Remove(adorner);
        }
        _adorners.Clear();
    }

    private static IEnumerable<FrameworkElement> FindElementsWithRole(DependencyObject root, string roleKey)
    {
        if (root is FrameworkElement fe
            && ThemeRoles.Parse(ThemeRoles.GetRoles(fe))
                .Any(r => string.Equals(r, roleKey, StringComparison.Ordinal)))
            yield return fe;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            foreach (var match in FindElementsWithRole(VisualTreeHelper.GetChild(root, i), roleKey))
                yield return match;
        }
    }

    private void BeginThemePickMode()
    {
        if (_themePickMode) return;
        _themePickMode = true;
        PreviewMouseLeftButtonDown += ThemePick_Preview;
        Cursor = Cursors.Cross;
    }

    private void EndThemePickMode()
    {
        if (!_themePickMode) return;
        _themePickMode = false;
        PreviewMouseLeftButtonDown -= ThemePick_Preview;
        ClearValue(CursorProperty);
    }

    private void ThemePick_Preview(object sender, MouseButtonEventArgs e)
    {
        if (_themeWindow is not { IsLoaded: true })
        {
            EndThemePickMode();
            return;
        }
        var roles = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        DependencyObject? hit = null;
        VisualTreeHelper.HitTest(this, null, r =>
        {
            hit = r.VisualHit as DependencyObject;
            return HitTestResultBehavior.Stop;
        }, new PointHitTestParameters(e.GetPosition(this)));
        for (var current = hit; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            foreach (var role in ThemeRoles.Parse(ThemeRoles.GetRoles(current)))
            {
                if (seen.Add(role)) roles.Add(role);
            }
            if (current is Window) break;
        }
        _themeWindow.HighlightRoles(roles.Count == 0 ? ["PageBackgroundColor"] : roles);
        e.Handled = true;
    }
}
