namespace HulkReNaymer;

public static class RulePresets
{
    public static readonly string[] Names =
    [
        "",
        "Project documents",
        "Strip export prefix",
        "Photo date + sequence",
        "Title case + underscores",
        "Prefix parent folder",
        "MP3 artist - title",
        "Search and replace",
        "kebab-case",
        "slug"
    ];

    public static Rules Create(string name) => name switch
    {
        "Project documents" => new Rules
        {
            CaseMode = "title",
            ReplaceEnabled = true,
            Find = " ",
            ReplaceWith = "_",
            AddEnabled = true,
            Prefix = "PROJECT123_"
        },
        "Strip export prefix" => new Rules { ReplaceEnabled = true, Find = "EXPORT_20260914_", ReplaceWith = "" },
        "Photo date + sequence" => new Rules
        {
            NameEnabled = true,
            NameMode = "remove",
            DateEnabled = true,
            DateSource = "exif",
            DateFormat = "yyyy-MM-dd",
            AddEnabled = true,
            Suffix = "Site-Inspection",
            NumberingEnabled = true,
            NumberPad = 3
        },
        "Title case + underscores" => new Rules { CaseMode = "title", ReplaceEnabled = true, Find = " ", ReplaceWith = "_" },
        "Prefix parent folder" => new Rules { FolderEnabled = true, FolderMode = "prefix", FolderSeparator = "_" },
        "MP3 artist - title" => new Rules { NameEnabled = true, NameMode = "fixed", NameFixed = "{id3.artist} - {id3.title}" },
        "Search and replace" => new Rules { RegexEnabled = true, RegexApplyTo = "name" },
        "kebab-case" => new Rules { CaseMode = "kebab" },
        "slug" => new Rules { CaseMode = "slug" },
        _ => new Rules { WindowsSafe = true }
    };
}
