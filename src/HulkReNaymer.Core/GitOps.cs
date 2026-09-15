using System.Diagnostics;

namespace HulkReNaymer;

/// <summary>
/// Git helpers for tokens and tracked-file moves. Never throws to callers; failures fall back.
/// </summary>
public static class GitOps
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    public static string? FindRepoRoot(string path)
    {
        string? current;
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (Directory.Exists(path) && !File.Exists(path))
                current = Path.GetFullPath(path);
            else
            {
                var parent = Path.GetDirectoryName(Path.GetFullPath(path));
                current = string.IsNullOrEmpty(parent) ? null : parent;
            }
        }
        catch
        {
            return null;
        }

        while (!string.IsNullOrEmpty(current))
        {
            var git = Path.Combine(current, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return current;
            current = Directory.GetParent(current)?.FullName;
        }
        return null;
    }

    public static string CurrentBranch(string path)
    {
        var repo = FindRepoRoot(path);
        if (repo is null) return "";
        if (!TryRun(repo, ["rev-parse", "--abbrev-ref", "HEAD"], out var output) || output.Length == 0)
            return "";
        var branch = output.Trim();
        return branch is "HEAD" ? "" : branch;
    }

    public static bool IsTracked(string path)
    {
        var repo = FindRepoRoot(path);
        if (repo is null) return false;
        return TryRun(repo, ["ls-files", "--error-unmatch", "--", path], out _);
    }

    /// <summary>
    /// Rename a tracked path with <c>git mv</c> when source and dest share a repo. Returns false so callers can File.Move.
    /// </summary>
    public static bool TryMove(string source, string dest)
    {
        try
        {
            var srcRepo = FindRepoRoot(source);
            if (srcRepo is null) return false;
            var destHint = File.Exists(dest) || Directory.Exists(dest)
                ? dest
                : Path.GetDirectoryName(dest) ?? dest;
            var destRepo = FindRepoRoot(destHint);
            if (destRepo is null || !SamePath(srcRepo, destRepo)) return false;
            if (!IsTracked(source)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? destRepo);
            return TryRun(srcRepo, ["mv", "--", source, dest], out _);
        }
        catch
        {
            return false;
        }
    }

    static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    static bool TryRun(string repo, IReadOnlyList<string> args, out string stdout)
    {
        stdout = "";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = repo,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(repo);
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);
            psi.Environment.Remove("GIT_DIR");
            psi.Environment.Remove("GIT_WORK_TREE");
            psi.Environment.Remove("GIT_INDEX_FILE");

            using var proc = Process.Start(psi);
            if (proc is null) return false;
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return false;
            }
            stdout = stdoutTask.GetAwaiter().GetResult();
            _ = stderrTask.GetAwaiter().GetResult();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
