using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace LaunchControl.Standard.Host;

public sealed record VenvLocker(int Pid, string Name, string Path, string CommandLine);

public sealed record VenvProbeResult(IReadOnlyList<VenvLocker> Lockers, string? Error);

/// <summary>
/// Lists Python hosts that have this product's venv loaded. Other products
/// (Heimdall, Switcheroo, Serraview Insights) use separate venvs and do not
/// lock this install's pywintypes DLL.
/// </summary>
public static class VenvProcessProbe
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr handle, uint flags, StringBuilder name, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    public static string[] DefaultNeedles(string installRoot)
    {
        var root = Path.GetFullPath(installRoot).TrimEnd('\\', '/');
        return
        [
            Path.Combine(root, ".venv"),
            Path.Combine(root, "venv"),
            Path.Combine(root, ".venv", "Scripts", "python.exe"),
            Path.Combine(root, "venv", "Scripts", "python.exe"),
            Path.Combine(root, "run_waitress.py"),
            Path.Combine(root, "run.py"),
            "-m app",
            "-m\tapp"
        ];
    }

    public static VenvProbeResult Find(string installRoot, IEnumerable<string>? serviceNames = null)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
            return new([], "Install folder is not set.");

        string[] needles;
        try
        {
            needles = DefaultNeedles(installRoot);
        }
        catch (Exception ex)
        {
            return new([], ex.Message);
        }

        try
        {
            var rows = QueryPythonRows();
            var servicePids = new HashSet<int>();
            foreach (var name in serviceNames ?? [])
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                TryAddServiceRow(rows, servicePids, name.Trim());
            }

            var matched = new Dictionary<int, VenvLocker>();
            var root = Path.GetFullPath(installRoot).TrimEnd('\\', '/');
            foreach (var row in rows.Values)
            {
                if (MatchesNeedle(row.Path, row.CommandLine, needles, root)
                    || servicePids.Contains(row.Pid)
                    || servicePids.Contains(row.ParentPid))
                    matched[row.Pid] = ToLocker(row);
            }

            // Waitress / pip children of pythonservice often have blank CIM CommandLine
            // when Launch Control is not elevated.
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var row in rows.Values)
                {
                    if (matched.ContainsKey(row.Pid)) continue;
                    if (row.ParentPid > 0 && matched.ContainsKey(row.ParentPid))
                    {
                        matched[row.Pid] = ToLocker(row);
                        changed = true;
                    }
                }
            }

            return new(matched.Values.OrderBy(x => x.Pid).ToList(), null);
        }
        catch (Exception ex)
        {
            return new([], "Win32_Process query failed: " + ex.Message);
        }
    }

    /// <summary>
    /// After Stop-Service, pythonservice.exe / WinSW children often keep
    /// the venv (pywintypes DLL) locked. Wait briefly, then kill remaining
    /// python.exe / pythonw.exe / pythonservice.exe whose path is this venv
    /// or whose command line is this install's python -m app.
    /// </summary>
    public static VenvProbeResult StopRemaining(
        string installRoot,
        IEnumerable<string>? serviceNames,
        TimeSpan timeout)
    {
        var found = Find(installRoot, serviceNames);
        if (found.Lockers.Count == 0)
            return found;

        var gracefulUntil = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < gracefulUntil)
        {
            Thread.Sleep(300);
            found = Find(installRoot, serviceNames);
            if (found.Lockers.Count == 0)
                return found;
        }

        var deadline = DateTime.UtcNow + (timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(15) : timeout);
        while (found.Lockers.Count > 0 && DateTime.UtcNow < deadline)
        {
            foreach (var locker in found.Lockers)
                TryKill(locker.Pid);
            Thread.Sleep(400);
            found = Find(installRoot, serviceNames);
        }

        return found;
    }

    private static void TryKill(int pid)
    {
        if (pid <= 0 || pid == Environment.ProcessId)
            return;
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited)
                return;
            p.Kill(entireProcessTree: true);
            return;
        }
        catch (ArgumentException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch
        {
            // Fall through to taskkill.
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                Arguments = $"/F /PID {pid} /T",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit(5000);
        }
        catch
        {
            // Remaining lockers are reported to the caller.
        }
    }

    public static string FormatLine(VenvLocker locker)
    {
        var cmd = string.IsNullOrWhiteSpace(locker.CommandLine) ? locker.Path : locker.CommandLine;
        if (string.IsNullOrWhiteSpace(cmd))
            cmd = "(command line unavailable — try Run as administrator)";
        return $"PID {locker.Pid}  {locker.Name}  {cmd}";
    }

    private sealed class ProcRow
    {
        public int Pid;
        public int ParentPid;
        public string Name = "";
        public string Path = "";
        public string CommandLine = "";
    }

    private static VenvLocker ToLocker(ProcRow row) =>
        new(row.Pid, row.Name, row.Path, row.CommandLine);

    private static Dictionary<int, ProcRow> QueryPythonRows()
    {
        var rows = new Dictionary<int, ProcRow>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId, ParentProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process " +
            "WHERE Name='python.exe' OR Name='pythonw.exe' OR Name='pythonservice.exe'");
        foreach (ManagementObject obj in searcher.Get())
        {
            using (obj)
            {
                var pid = Convert.ToInt32(obj["ProcessId"] ?? 0);
                if (pid <= 0) continue;
                var path = obj["ExecutablePath"]?.ToString() ?? "";
                var cmd = obj["CommandLine"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(path))
                    path = QueryImagePath(pid) ?? "";
                rows[pid] = new ProcRow
                {
                    Pid = pid,
                    ParentPid = Convert.ToInt32(obj["ParentProcessId"] ?? 0),
                    Name = obj["Name"]?.ToString() ?? "",
                    Path = path,
                    CommandLine = cmd
                };
            }
        }
        return rows;
    }

    private static void TryAddServiceRow(Dictionary<int, ProcRow> rows, HashSet<int> servicePids, string serviceName)
    {
        try
        {
            var filter = "Name='" + serviceName.Replace("'", "''") + "'";
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, PathName FROM Win32_Service WHERE " + filter);
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var pid = Convert.ToInt32(obj["ProcessId"] ?? 0);
                    var pathName = (obj["PathName"]?.ToString() ?? "").Trim().Trim('"');
                    if (pid <= 0)
                        continue;
                    servicePids.Add(pid);
                    if (string.IsNullOrWhiteSpace(pathName))
                        continue;
                    if (rows.TryGetValue(pid, out var existing))
                    {
                        if (string.IsNullOrWhiteSpace(existing.Path))
                            existing.Path = pathName;
                        if (string.IsNullOrWhiteSpace(existing.CommandLine))
                            existing.CommandLine = pathName;
                        continue;
                    }

                    var leaf = Path.GetFileName(pathName);
                    if (string.IsNullOrWhiteSpace(leaf))
                        leaf = "service";

                    rows[pid] = new ProcRow
                    {
                        Pid = pid,
                        Name = leaf,
                        Path = pathName,
                        CommandLine = pathName
                    };
                }
            }
        }
        catch
        {
            // Service query is a fallback; process list still applies.
        }
    }

    private static string? QueryImagePath(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = (uint)sb.Capacity;
            if (!QueryFullProcessImageName(handle, 0, sb, ref size))
                return null;
            return sb.ToString();
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static bool MatchesNeedle(string path, string cmd, string[] needles, string installRoot)
    {
        foreach (var n in needles)
        {
            if (n.StartsWith("-m", StringComparison.Ordinal))
            {
                var inThisInstall = ContainsPath(path, installRoot) || ContainsPath(cmd, installRoot);
                if (inThisInstall && (ContainsModuleArg(cmd, n) || ContainsModuleArg(path, n)))
                    return true;
                continue;
            }
            if (ContainsPath(path, n) || ContainsPath(cmd, n))
                return true;
        }
        return false;
    }

    private static bool ContainsModuleArg(string hay, string needle)
    {
        if (string.IsNullOrEmpty(hay) || string.IsNullOrEmpty(needle))
            return false;
        return hay.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsPath(string hay, string needle)
    {
        if (string.IsNullOrEmpty(hay) || string.IsNullOrEmpty(needle))
            return false;
        if (hay.Contains(needle, StringComparison.OrdinalIgnoreCase))
            return true;
        var fwd = needle.Replace('\\', '/');
        return !string.Equals(fwd, needle, StringComparison.Ordinal)
               && hay.Contains(fwd, StringComparison.OrdinalIgnoreCase);
    }
}
