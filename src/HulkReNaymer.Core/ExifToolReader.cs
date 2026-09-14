using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace HulkReNaymer;

/// <summary>
/// Optional ExifTool front-end. Invoked only when HULKRENAYMER_EXIFTOOL points at a binary.
/// Never required: missing binary, unset env, or a failed run returns an empty map.
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
        // Opt-in only. A system exiftool on PATH must not run during folder scans
        // (one process spawn / up to 4s per file). If the env var is set but the
        // path is missing, do not fall through to PATH discovery.
        var configured = Environment.GetEnvironmentVariable(PathEnv);
        if (string.IsNullOrWhiteSpace(configured)) return null;
        try
        {
            return File.Exists(configured) ? configured : null;
        }
        catch
        {
            return null;
        }
    }

    public static Dictionary<string, string> Read(string path)
    {
        if (!ExplicitlyConfigured) return [];
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
        data[key] = key switch
        {
            "date" or "video.date" => NormalizeDate(text),
            "video.duration" => NormalizeDuration(text),
            _ => text
        };
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

    /// <summary>
    /// ExifTool <c>-n</c> prints duration as seconds (e.g. 65.5). TagLib uses <c>MM-SS</c> / <c>HH-MM-SS</c>.
    /// </summary>
    public static string NormalizeDuration(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        raw = raw.Trim();
        if (raw.Contains(':'))
        {
            if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var parsed))
                return VideoMetadata.FormatDuration(parsed);
            return raw;
        }
        var numeric = raw.EndsWith(" s", StringComparison.OrdinalIgnoreCase) ? raw[..^2].Trim() : raw;
        if (double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return VideoMetadata.FormatDuration(TimeSpan.FromSeconds(Math.Max(seconds, 0)));
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
