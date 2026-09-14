using System.Text.RegularExpressions;

namespace HulkReNaymer;

public static class Names
{
    static readonly Regex WindowsIllegal = new(@"[<>:""/\\|?*]", RegexOptions.Compiled);
    static readonly HashSet<string> Reserved =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    public static (string Stem, string Ext) SplitName(string filename)
    {
        if (filename is "." or "..") return (filename, "");
        var lastDot = filename.LastIndexOf('.');
        if (lastDot <= 0) return (filename, "");
        return (filename[..lastDot], filename[lastDot..]);
    }

    public static string JoinName(string stem, string ext)
    {
        if (string.IsNullOrEmpty(ext)) return stem;
        if (!ext.StartsWith('.')) ext = "." + ext;
        return stem + ext;
    }

    public static string WindowsSafeName(string name)
    {
        var (stem, ext) = SplitName(name);
        stem = WindowsIllegal.Replace(stem, "_").TrimEnd(' ', '.');
        var extBody = WindowsIllegal.Replace(ext.TrimStart('.'), "_");
        if (string.IsNullOrEmpty(stem)) stem = "_";
        if (Reserved.Contains(stem.ToUpperInvariant())) stem = "_" + stem;
        return JoinName(stem, string.IsNullOrEmpty(extBody) ? "" : "." + extBody);
    }

    public static string ConvertDateFormat(string format)
    {
        if (!format.Contains('%')) return format;
        return format
            .Replace("%Y", "yyyy", StringComparison.Ordinal)
            .Replace("%m", "MM", StringComparison.Ordinal)
            .Replace("%d", "dd", StringComparison.Ordinal)
            .Replace("%H", "HH", StringComparison.Ordinal)
            .Replace("%M", "mm", StringComparison.Ordinal)
            .Replace("%S", "ss", StringComparison.Ordinal);
    }
}

public sealed class NaturalStringComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        var ax = Regex.Split(x, @"(\d+)");
        var ay = Regex.Split(y, @"(\d+)");
        var n = Math.Min(ax.Length, ay.Length);
        for (var i = 0; i < n; i++)
        {
            if (int.TryParse(ax[i], out var nx) && int.TryParse(ay[i], out var ny))
            {
                var cmp = nx.CompareTo(ny);
                if (cmp != 0) return cmp;
            }
            else
            {
                var cmp = string.Compare(ax[i], ay[i], StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
            }
        }
        return ax.Length.CompareTo(ay.Length);
    }
}
