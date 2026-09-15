using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace HulkReNaymer;

public static class Tokens
{
    static readonly Regex BraceRe = new(@"\{([^{}]+)\}", RegexOptions.Compiled);
    static readonly Regex PowerCounterRe = new(@"\$\{(?:|(?:n(?::(\d+))?)|(?:padding=(\d+)))\}", RegexOptions.Compiled);
    static readonly Regex DollarAliasRe = new(
        @"\$(YYYY|YY|MMMM|MMM|MM|DDDD|DDD|DD|hh|mm|ss|fff|ff|Y|M|D|h|m|s|f)",
        RegexOptions.Compiled);

    public static string Expand(string template, FileItem item, Rules rules, int sequence, bool preserveRegexGroups = false)
    {
        if (string.IsNullOrEmpty(template)) return template;
        if (template.Contains('$'))
            template = NormalizeAliases(template, preserveRegexGroups);
        if (!template.Contains('{')) return template;

        var chosen = ItemDate(item, rules.DateSource);
        var pad = Math.Max(rules.NumberPad, 1);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = item.Stem,
            ["ext"] = item.Ext.TrimStart('.'),
            ["folder"] = item.Folder,
            ["parent"] = item.Folder,
            ["n"] = sequence.ToString().PadLeft(pad, '0'),
            ["N"] = sequence.ToString(),
            ["size"] = item.Size.ToString(),
            ["date"] = FormatDate(chosen, rules.DateFormat),
            ["yyyy"] = FormatDate(chosen, "yyyy"),
            ["yy"] = FormatDate(chosen, "yy"),
            ["y"] = chosen is null ? "" : (chosen.Value.Year % 10).ToString(CultureInfo.InvariantCulture),
            ["mm"] = FormatDate(chosen, "MM"),
            ["MM"] = FormatDate(chosen, "MM"),
            ["M"] = FormatDate(chosen, "M"),
            ["MMMM"] = FormatDate(chosen, "MMMM"),
            ["MMM"] = FormatDate(chosen, "MMM"),
            ["dd"] = FormatDate(chosen, "dd"),
            ["d"] = FormatDate(chosen, "d"),
            ["dddd"] = FormatDate(chosen, "dddd"),
            ["ddd"] = FormatDate(chosen, "ddd"),
            ["DDDD"] = FormatDate(chosen, "dddd"),
            ["DDD"] = FormatDate(chosen, "ddd"),
            ["DD"] = FormatDate(chosen, "dd"),
            ["hh"] = FormatDate(chosen, "HH"),
            ["HH"] = FormatDate(chosen, "HH"),
            ["H"] = FormatDate(chosen, "H"),
            ["nn"] = FormatDate(chosen, "mm"),
            ["min"] = FormatDate(chosen, "mm"),
            ["ss"] = FormatDate(chosen, "ss"),
            ["s"] = FormatDate(chosen, "s"),
            ["fff"] = FormatDate(chosen, "fff"),
            ["ff"] = FormatDate(chosen, "ff"),
            ["f"] = FormatDate(chosen, "f"),
            ["exif.date"] = item.Exif.GetValueOrDefault("date", ""),
            ["exif.width"] = item.Exif.GetValueOrDefault("width", ""),
            ["exif.height"] = item.Exif.GetValueOrDefault("height", ""),
            ["exif.make"] = item.Exif.GetValueOrDefault("make", ""),
            ["exif.model"] = item.Exif.GetValueOrDefault("model", ""),
            ["exif.camera"] = CameraName(item.Exif),
            ["exif.iso"] = item.Exif.GetValueOrDefault("iso", ""),
            ["exif.fnumber"] = item.Exif.GetValueOrDefault("fnumber", ""),
            ["id3.artist"] = item.Id3.GetValueOrDefault("artist", ""),
            ["id3.album"] = item.Id3.GetValueOrDefault("album", ""),
            ["id3.title"] = item.Id3.GetValueOrDefault("title", ""),
            ["video.duration"] = item.Props.GetValueOrDefault("video.duration", ""),
            ["video.date"] = item.Props.GetValueOrDefault("video.date", ""),
            ["video.width"] = item.Props.GetValueOrDefault("video.width", ""),
            ["video.height"] = item.Props.GetValueOrDefault("video.height", "")
        };

        return BraceRe.Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            if (key.StartsWith("n:", StringComparison.Ordinal))
            {
                if (!int.TryParse(key[2..], out var width)) width = pad;
                return sequence.ToString().PadLeft(Math.Max(width, 1), '0');
            }
            if (key.StartsWith("date:", StringComparison.Ordinal))
                return FormatDate(ItemDate(item, key[5..]), rules.DateFormat);
            if (key.Equals("git.branch", StringComparison.OrdinalIgnoreCase))
                return GitOps.CurrentBranch(item.Path);
            if (key.Equals("hash", StringComparison.OrdinalIgnoreCase))
                return ContentHash(item, 8);
            if (key.StartsWith("hash:", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(key[5..], out var width)) width = 8;
                return ContentHash(item, width);
            }
            return values.TryGetValue(key, out var value) ? value : match.Value;
        });
    }

    static string NormalizeAliases(string template, bool preserveRegexGroups)
    {
        template = PowerCounterRe.Replace(template, match =>
        {
            var width = match.Groups[1].Success ? match.Groups[1].Value
                : match.Groups[2].Success ? match.Groups[2].Value
                : "";
            return string.IsNullOrEmpty(width) ? "{n}" : "{n:" + width + "}";
        });

        return DollarAliasRe.Replace(template, match =>
        {
            var token = match.Groups[1].Value;
            if (preserveRegexGroups && token.All(char.IsDigit))
                return match.Value;
            return token switch
            {
                "YYYY" => "{yyyy}",
                "YY" => "{yy}",
                "Y" => "{y}",
                "MMMM" => "{MMMM}",
                "MMM" => "{MMM}",
                "MM" => "{mm}",
                "M" => "{M}",
                "DDDD" => "{dddd}",
                "DDD" => "{ddd}",
                "DD" => "{dd}",
                "D" => "{d}",
                "hh" => "{hh}",
                "h" => "{H}",
                "mm" => "{nn}",
                "m" => "{min}",
                "ss" => "{ss}",
                "s" => "{s}",
                "fff" => "{fff}",
                "ff" => "{ff}",
                "f" => "{f}",
                _ => match.Value
            };
        });
    }

    internal static DateTime? ItemDate(FileItem item, string source) => source switch
    {
        "created" => item.Created,
        "accessed" => item.Accessed,
        "now" => DateTime.Now,
        "exif" => RenameEngine.ParseExifDate(item) ?? item.Modified,
        _ => item.Modified
    };

    static string FormatDate(DateTime? value, string format)
    {
        if (value is null) return "";
        var converted = Names.ConvertDateFormat(format);
        try { return value.Value.ToString(converted, CultureInfo.InvariantCulture); }
        catch { return value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
    }

    static string CameraName(Dictionary<string, string> exif)
    {
        if (exif.TryGetValue("camera", out var camera) && !string.IsNullOrWhiteSpace(camera))
            return camera;
        var make = exif.GetValueOrDefault("make", "").Trim();
        var model = exif.GetValueOrDefault("model", "").Trim();
        if (make.Length == 0) return model;
        if (model.Length == 0) return make;
        if (model.StartsWith(make, StringComparison.OrdinalIgnoreCase))
            return model;
        return (make + " " + model).Trim();
    }

    static string ContentHash(FileItem item, int width)
    {
        width = Math.Clamp(width, 1, 64);
        if (item.IsDir) return "";
        try
        {
            if (!File.Exists(item.Path)) return "";
            using var stream = File.OpenRead(item.Path);
            var hash = SHA256.HashData(stream);
            var hex = Convert.ToHexString(hash).ToLowerInvariant();
            return hex[..Math.Min(width, hex.Length)];
        }
        catch
        {
            return "";
        }
    }
}
