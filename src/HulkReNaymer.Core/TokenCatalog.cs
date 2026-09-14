namespace HulkReNaymer;

public sealed record TokenSpec(string Token, string Label);

public static class TokenCatalog
{
    public static readonly TokenSpec[] All =
    [
        new("{name}", "name"),
        new("{ext}", "ext"),
        new("{folder}", "folder"),
        new("{n}", "n"),
        new("{n:3}", "n:3"),
        new("{date}", "date"),
        new("{yyyy}", "yyyy"),
        new("{mm}", "mm"),
        new("{dd}", "dd"),
        new("{exif.date}", "exif.date"),
        new("{exif.width}", "exif.w"),
        new("{exif.height}", "exif.h"),
        new("{exif.make}", "make"),
        new("{exif.model}", "model"),
        new("{exif.camera}", "camera"),
        new("{id3.artist}", "artist"),
        new("{id3.album}", "album"),
        new("{id3.title}", "title"),
        new("{git.branch}", "branch"),
        new("{hash:8}", "hash"),
        new("{video.date}", "vid date"),
        new("{video.duration}", "vid len"),
        new("{size}", "size")
    ];

    public static readonly HashSet<string> TargetBoxNames = new(StringComparer.Ordinal)
    {
        "RegexReplace", "NameFixed", "ExtFixed", "ReplaceWithBox",
        "PrefixBox", "SuffixBox", "InsertBox", "DestDirBox"
    };

    public static string Insert(string text, int caret, string token, out int newCaret)
    {
        text ??= "";
        caret = Math.Clamp(caret, 0, text.Length);
        var result = text.Insert(caret, token);
        newCaret = caret + token.Length;
        return result;
    }
}
