using System.Text.Json;

namespace LaunchControl.Standard.Compat;

public sealed class LcCompatDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ThemePath { get; set; } = "";
    public string AppDataFolder { get; set; } = "";
}

public static class LcCompat
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string ThemePathFor(string appDataFolderName) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            appDataFolderName,
            "theme.json");

    public static void Write(string exeDirectory, string productId, string productName, string appDataFolderName)
    {
        var themePath = ThemePathFor(appDataFolderName);
        var doc = new LcCompatDocument
        {
            SchemaVersion = SchemaVersion,
            ProductId = productId,
            ProductName = productName,
            ThemePath = themePath,
            AppDataFolder = appDataFolderName
        };
        var json = JsonSerializer.Serialize(doc, Json);

        try
        {
            if (!string.IsNullOrWhiteSpace(exeDirectory))
            {
                Directory.CreateDirectory(exeDirectory);
                File.WriteAllText(Path.Combine(exeDirectory, "lc-compat.json"), json);
            }
        }
        catch { }

        try
        {
            var appDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appDataFolderName);
            Directory.CreateDirectory(appDir);
            File.WriteAllText(Path.Combine(appDir, "lc-compat.json"), json);
        }
        catch { }
    }

    public static LcCompatDocument? TryDiscover(string? launchPath)
    {
        if (string.IsNullOrWhiteSpace(launchPath))
            return null;

        foreach (var candidate in EnumerateCandidatePaths(launchPath))
        {
            var doc = TryRead(candidate);
            if (doc is not null && doc.SchemaVersion >= 1 && !string.IsNullOrWhiteSpace(doc.ThemePath))
                return doc;
        }

        if (HasStandardExe(launchPath))
        {
            foreach (var folder in GuessAppDataFolders(launchPath))
            {
                return new LcCompatDocument
                {
                    SchemaVersion = SchemaVersion,
                    ProductId = folder.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant(),
                    ProductName = folder,
                    ThemePath = ThemePathFor(folder),
                    AppDataFolder = folder
                };
            }
        }

        return null;
    }

    public static string IncompatibleReason(string? launchPath)
    {
        var ext = Path.GetExtension(launchPath ?? "");
        if (ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
            return "This Launch Control is the older PowerShell UI and does not support themes.";
        if (TryDiscover(launchPath) is null)
            return "No lc-compat.json found — this Launch Control is not Standard-compatible yet.";
        return "Not compatible.";
    }

    private static IEnumerable<string> EnumerateCandidatePaths(string launchPath)
    {
        string? dir;
        try { dir = Path.GetDirectoryName(Path.GetFullPath(launchPath)); }
        catch { yield break; }
        if (string.IsNullOrEmpty(dir)) yield break;

        yield return Path.Combine(dir, "lc-compat.json");

        var exe = Path.ChangeExtension(launchPath, ".exe");
        if (!string.IsNullOrEmpty(exe))
        {
            var exeDir = Path.GetDirectoryName(exe);
            if (!string.IsNullOrEmpty(exeDir))
                yield return Path.Combine(exeDir, "lc-compat.json");
        }

        var parent = Directory.GetParent(dir);
        var root = parent is not null && dir.EndsWith("scripts", StringComparison.OrdinalIgnoreCase)
            ? parent.FullName
            : dir;

        foreach (var config in new[] { "Release", "Debug" })
        {
            var bin = Path.Combine(root, "launch-control", "bin", config, "net8.0-windows", "lc-compat.json");
            yield return bin;
        }

        foreach (var folder in GuessAppDataFolders(launchPath))
        {
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                folder, "lc-compat.json");
        }
    }

    private static bool HasStandardExe(string launchPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(launchPath));
            if (string.IsNullOrEmpty(dir)) return false;
            var root = dir.EndsWith("scripts", StringComparison.OrdinalIgnoreCase)
                ? Directory.GetParent(dir)?.FullName
                : dir;
            if (string.IsNullOrEmpty(root)) return false;
            foreach (var config in new[] { "Release", "Debug" })
            {
                var bin = Path.Combine(root, "launch-control", "bin", config, "net8.0-windows");
                if (Directory.Exists(bin) && Directory.EnumerateFiles(bin, "*LaunchControl.exe").Any())
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static IEnumerable<string> GuessAppDataFolders(string launchPath)
    {
        if (launchPath.Contains("Heimdall", StringComparison.OrdinalIgnoreCase))
            yield return "Heimdall";
        if (launchPath.Contains("Switcheroo", StringComparison.OrdinalIgnoreCase))
            yield return "Switcheroo";
        if (launchPath.Contains("Serraview", StringComparison.OrdinalIgnoreCase))
            yield return "SerraviewInsights";
    }

    private static LcCompatDocument? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<LcCompatDocument>(File.ReadAllText(path), Json);
        }
        catch
        {
            return null;
        }
    }
}
