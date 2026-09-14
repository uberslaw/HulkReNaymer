namespace HulkReNaymer;

public static class FileSelection
{
    /// <summary>
    /// First listing of a folder selects everything. Later refreshes keep the
    /// user's checks, including an empty selection.
    /// </summary>
    public static void Apply(IEnumerable<FileItem> items, IEnumerable<string> previouslySelected, bool hadRows)
    {
        if (!hadRows) return;
        var selected = new HashSet<string>(previouslySelected, StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
            item.Selected = selected.Contains(item.Path);
    }
}
