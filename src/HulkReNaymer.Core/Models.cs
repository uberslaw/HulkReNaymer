namespace HulkReNaymer;

public sealed class FileItem
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required string Stem { get; init; }
    public required string Ext { get; init; }
    public required string Folder { get; init; }
    public required string Parent { get; init; }
    public bool IsDir { get; init; }
    public long Size { get; init; }
    public DateTime? Created { get; init; }
    public DateTime? Modified { get; init; }
    public DateTime? Accessed { get; init; }
    public int Depth { get; set; }
    public bool Selected { get; set; } = true;
    public Dictionary<string, string> Exif { get; init; } = new();
    public Dictionary<string, string> Id3 { get; init; } = new();
    public Dictionary<string, string> Props { get; init; } = new();
}

public sealed class Rules
{
    public bool RegexEnabled { get; set; }
    public string RegexPattern { get; set; } = "";
    public string RegexReplace { get; set; } = "";
    public string RegexApplyTo { get; set; } = "name";

    public bool NameEnabled { get; set; }
    public string NameMode { get; set; } = "keep";
    public string NameFixed { get; set; } = "";
    public string ExtMode { get; set; } = "keep";
    public string ExtFixed { get; set; } = "";

    public bool ReplaceEnabled { get; set; }
    public string Find { get; set; } = "";
    public string ReplaceWith { get; set; } = "";
    public bool ReplaceAll { get; set; } = true;
    public bool ReplaceCaseSensitive { get; set; } = true;
    public string ReplaceApplyTo { get; set; } = "name";

    public string CaseMode { get; set; } = "same";
    public string CaseApplyTo { get; set; } = "name";

    public bool RemoveEnabled { get; set; }
    public int RemoveFirstN { get; set; }
    public int RemoveLastN { get; set; }
    public int RemoveFrom { get; set; }
    public int RemoveTo { get; set; }
    public string RemoveChars { get; set; } = "";
    public string RemoveWords { get; set; } = "";
    public int RemoveFirstWords { get; set; }
    public int RemoveLastWords { get; set; }
    public bool RemoveDigits { get; set; }
    public bool RemoveSymbols { get; set; }
    public bool RemoveLetters { get; set; }
    public bool RemoveTrim { get; set; }
    public bool CollapseSpaces { get; set; }

    public bool MoveEnabled { get; set; }
    public int MoveFrom { get; set; }
    public int MoveCount { get; set; }
    public int MoveTo { get; set; }
    public bool MoveCopy { get; set; }

    public bool AddEnabled { get; set; }
    public string Prefix { get; set; } = "";
    public string Suffix { get; set; } = "";
    public string Insert { get; set; } = "";
    public int InsertAt { get; set; }

    public bool FolderEnabled { get; set; }
    public string FolderMode { get; set; } = "prefix";
    public string FolderSeparator { get; set; } = "_";

    public bool NumberingEnabled { get; set; }
    public int NumberStart { get; set; } = 1;
    public int NumberIncrement { get; set; } = 1;
    public int NumberPad { get; set; } = 3;
    public string NumberSeparator { get; set; } = "_";
    public string NumberPosition { get; set; } = "suffix";
    public int NumberInsertAt { get; set; }
    public bool NumberResetPerFolder { get; set; }

    public bool DateEnabled { get; set; }
    public string DateSource { get; set; } = "modified";
    public string DateFormat { get; set; } = "yyyy-MM-dd";
    public string DatePosition { get; set; } = "prefix";
    public string DateSeparator { get; set; } = "_";

    public bool JsEnabled { get; set; }
    public string JsCode { get; set; } = "";

    public Dictionary<string, string> Mapping { get; set; } = new();
    public bool MappingExclusive { get; set; } = true;

    public bool WindowsSafe { get; set; } = true;
    public string Operation { get; set; } = "rename";
    public string DestDir { get; set; } = "";

    public bool SetTimestamps { get; set; }
    public string TsModified { get; set; } = "";
    public string TsAccessed { get; set; } = "";
    public bool SetReadonly { get; set; }
    public bool ReadonlyValue { get; set; }
    public bool SetHidden { get; set; }
    public bool HiddenValue { get; set; }

    public Rules Clone()
    {
        var copy = (Rules)MemberwiseClone();
        copy.Mapping = new Dictionary<string, string>(Mapping);
        return copy;
    }
}

public sealed class PreviewRow
{
    public required string Path { get; init; }
    public required string OldName { get; init; }
    public required string NewName { get; init; }
    public required string NewPath { get; init; }
    public bool IsDir { get; init; }
    public long Size { get; init; }
    public string? Created { get; init; }
    public string? Modified { get; init; }
    public string? Accessed { get; init; }
    public bool Selected { get; init; }
    public bool Changed { get; init; }
    public string Status { get; set; } = "ok";
    public string Warning { get; set; } = "";
    public string Folder { get; init; } = "";
    public string Ext { get; init; } = "";
}

public sealed class CommitResult
{
    public int Renamed { get; init; }
    public string Message { get; init; } = "";
    public string? UndoId { get; init; }
    public List<FailedItem> Failed { get; init; } = [];
}

public sealed class FailedItem
{
    public required string Path { get; init; }
    public required string Error { get; init; }
}

public sealed class Favorite
{
    public required string Name { get; set; }
    public required Rules Rules { get; set; }
}
