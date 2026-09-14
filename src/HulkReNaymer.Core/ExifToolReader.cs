using System.Diagnostics;
using System.Text.Json;

namespace HulkReNaymer;

/// <summary>
/// Optional ExifTool front-end. Never required: missing binary or a failed run returns an empty map.
/// </summary>
public static class ExifToolReader
{
    public const string PathEnv = "HULKRENAYMER_EXIFTOOL";
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);
    static readonly object Gate = new();
    static string? _resolved;
    static bool _looked;

    public static bool ExplicitlyConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PathEnv));

    public static bool IsAvailable => !string.IsNullOrEmpty(ResolvedPath());

    public static string? ResolvedPath()
    {
        lock (Gate)
        {
            if (_looked) return _resolved;
            _looked = true;
            _resolved = FindTool();
            return _resolved;
        }
    }

    /// <summary>Test helper so unit tests can reset PATH/env resolution.</summary>
    public static void ResetCache()
    {
        lock (Gate)
        {
            _looked = false;
            _resolved = null;
        }
    }

    static string? FindTool()
    {
        var configured = Environment.GetEnvironmentVariable(PathEnv);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        foreach (var name in new[] { "exiftool", "exiftool.exe" })
        {
            var fromPath = FindOnPath(name);
            if (fromPath is not null) return fromPath;
        }

        try
        {
            var beside = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "exiftool.exe" : "exiftool");
            if (File.Exists(beside)) return beside;
        }
        catch { /* ignore */ }
        return null;
    }

    static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* ignore */ }
        }
        return null;
    }

    public static Dictionary<string, string> Read(string path)
    {
        var tool = ResolvedPath();
        if (tool is null || !File.Exists(path)) return [];
        if (!TryRun(tool, path, out var json)) return [];
        return ParseJson(json);
    }

    public static Dictionary<string, string> ParseJson(string json)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return data;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var obj = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0
                ? root[0]
                : root.ValueKind == JsonValueKind.Object ? root : default;
            if (obj.ValueKind != JsonValueKind.Object) return data;

            Map(obj, data, "DateTimeOriginal", "date");
            Map(obj, data, "CreateDate", "date");
            Map(obj, data, "MediaCreateDate", "date");
            Map(obj, data, "TrackCreateDate", "date");
            Map(obj, data, "Make", "make");
            Map(obj, data, "Model", "model");
            Map(obj, data, "ImageWidth", "width");
            Map(obj, data, "ImageHeight", "height");
            Map(obj, data, "ISO", "iso");
            Map(obj, data, "FNumber", "fnumber");
            Map(obj, data, "FocalLength", "focal");
            Map(obj, data, "Duration", "video.duration");
            Map(obj, data, "MediaDuration", "video.duration");
            if (obj.TryGetProperty("CreateDate", out var created) || obj.TryGetProperty("MediaCreateDate", out created))
                data["video.date"] = NormalizeDate(Text(created));
            else if (obj.TryGetProperty("TrackCreateDate", out created))
                data["video.date"] = NormalizeDate(Text(created));
        }
        catch
        {
            return [];
        }
        return data;
    }

    static void Map(JsonElement obj, Dictionary<string, string> data, string jsonName, string key)
    {
        if (data.ContainsKey(key)) return;
        if (!obj.TryGetProperty(jsonName, out var value)) return;
        var text = Text(value);
        if (string.IsNullOrWhiteSpace(text)) return;
        data[key] = key is "date" or "video.date" ? NormalizeDate(text) : text;
    }

    static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => ""
    };

    internal static string NormalizeDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        raw = raw.Trim();
        if (raw.Length >= 19 && raw[4] == ':')
            return raw[..10].Replace(':', '-') + raw[10..];
        if (raw.Length >= 10 && raw[4] == ':')
            return raw[..10].Replace(':', '-');
        return raw;
    }

    static bool TryRun(string tool, string file, out string stdout)
    {
        stdout = "";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = tool,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-j");
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add("-fast");
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(file);
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
            return proc.ExitCode == 0 && stdout.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
