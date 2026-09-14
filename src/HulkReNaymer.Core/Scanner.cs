using System.Text.RegularExpressions;

namespace HulkReNaymer;

public static class Scanner
{
    static readonly HashSet<string> SkipNames = [".git", ".svn", "node_modules", "__pycache__", ".vs"];

    public static (List<FileItem> Items, string Warning) Scan(
        string path,
        bool recurse = false,
        bool includeFiles = true,
        bool includeFolders = false,
        string wildcard = "*",
        string nameRegex = "",
        int minLength = 0,
        int maxLength = 0,
        int maxFiles = 8000,
        bool includeHidden = false,
        int maxDepth = 0)
    {
        string root;
        try { root = Path.GetFullPath(path); }
        catch (Exception ex) { return ([], ex.Message); }

        if (!Directory.Exists(root))
            return ([], "Path does not exist: " + root);

        Regex? compiled = string.IsNullOrEmpty(nameRegex) ? null : new Regex(nameRegex);
        var items = new List<FileItem>();
        var warning = "";

        void Consider(string entry, int depth)
        {
            if (items.Count >= maxFiles) return;
            var name = Path.GetFileName(entry);
            if (!includeHidden && name.StartsWith('.')) return;
            if (SkipNames.Contains(name)) return;
            bool isDir;
            try { isDir = Directory.Exists(entry) && !File.Exists(entry); }
            catch { return; }
            if (isDir && !includeFolders) return;
            if (!isDir && !includeFiles) return;
            if (!MatchWildcard(name, wildcard)) return;
            if (compiled is not null && !compiled.IsMatch(name)) return;
            if (minLength > 0 && name.Length < minLength) return;
            if (maxLength > 0 && name.Length > maxLength) return;
            try
            {
                var item = MetadataReader.Describe(entry, root);
                item.Selected = true;
                item.Depth = depth;
                items.Add(item);
            }
            catch { /* skip unreadable */ }
        }

        if (recurse)
        {
            void Walk(string dir, int depth)
            {
                if (items.Count >= maxFiles) return;
                if (maxDepth > 0 && depth > maxDepth) return;
                if (includeFolders && dir != root) Consider(dir, depth);
                if (includeFiles)
                {
                    IEnumerable<string> files;
                    try { files = Directory.EnumerateFiles(dir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase); }
                    catch { files = []; }
                    foreach (var file in files)
                    {
                        Consider(file, depth);
                        if (items.Count >= maxFiles) break;
                    }
                }
                if (items.Count >= maxFiles)
                {
                    warning = $"Listing truncated at {maxFiles} items";
                    return;
                }
                IEnumerable<string> children;
                try { children = Directory.EnumerateDirectories(dir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase); }
                catch { return; }
                foreach (var child in children)
                {
                    var name = Path.GetFileName(child);
                    if (SkipNames.Contains(name)) continue;
                    if (!includeHidden && name.StartsWith('.')) continue;
                    Walk(child, depth + 1);
                    if (items.Count >= maxFiles) return;
                }
            }
            Walk(root, 0);
        }
        else
        {
            try
            {
                var entries = Directory.EnumerateFileSystemEntries(root)
                    .OrderBy(p => Directory.Exists(p) ? 0 : 1)
                    .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries)
                {
                    Consider(entry, 0);
                    if (items.Count >= maxFiles)
                    {
                        warning = $"Listing truncated at {maxFiles} items";
                        break;
                    }
                }
            }
            catch (Exception ex) { return ([], ex.Message); }
        }
        return (items, warning);
    }

    public static (List<FileItem> Items, string Warning) FromPaths(
        IEnumerable<string> paths,
        bool includeFiles = true,
        bool includeFolders = true,
        bool includeHidden = false)
    {
        var items = new List<FileItem>();
        foreach (var raw in paths)
        {
            string path;
            try { path = Path.GetFullPath(raw); }
            catch { continue; }
            var name = Path.GetFileName(path);
            if (!includeHidden && name.StartsWith('.')) continue;
            try
            {
                if (Directory.Exists(path) && !File.Exists(path))
                {
                    if (!includeFolders) continue;
                    items.Add(MetadataReader.Describe(path));
                }
                else if (File.Exists(path))
                {
                    if (!includeFiles) continue;
                    items.Add(MetadataReader.Describe(path));
                }
            }
            catch { /* skip unreadable */ }
        }
        return (items, "");
    }

    public static IEnumerable<(string Name, string Path)> ListChildren(string path)
    {
        if (!Directory.Exists(path)) yield break;
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(path).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase); }
        catch { yield break; }
        foreach (var dir in dirs)
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.') || SkipNames.Contains(name)) continue;
            yield return (name, dir);
        }
    }

    static bool MatchWildcard(string name, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern is "*" or "*.*") return true;
        foreach (var part in pattern.Replace(';', ',').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (MatchesGlob(name, part)) return true;
        }
        return false;
    }

    static bool MatchesGlob(string name, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(name, regex, RegexOptions.IgnoreCase);
    }
}
