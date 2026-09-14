using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HulkReNaymer;

public static class RenameEngine
{
    static readonly Regex TitleSplit = new(@"([_\-\s]+)", RegexOptions.Compiled);
    static readonly Regex WordSplit = new(@"(\s+)", RegexOptions.Compiled);
    static readonly Regex DigitRe = new(@"\d", RegexOptions.Compiled);
    static readonly Regex SymbolRe = new(@"[\p{P}\p{S}]", RegexOptions.Compiled);
    static readonly Regex LetterRe = new(@"\p{L}", RegexOptions.Compiled);
    static readonly Regex LastNumberRe = new(@"(\d+)(?!.*\d)", RegexOptions.Compiled);
    static readonly NaturalStringComparer Natural = new();

    public static (string NewName, string Warning) ApplyRules(FileItem item, Rules rules, int sequence)
    {
        if (rules.Mapping.Count > 0 && TryLookupMapping(rules.Mapping, item, out var mapped))
        {
            if (rules.MappingExclusive)
                return (rules.WindowsSafe ? Names.WindowsSafeName(mapped) : mapped, "");
            var (mappedStem, mappedExt) = Names.SplitName(mapped);
            item = new FileItem
            {
                Path = item.Path,
                Name = mapped,
                Stem = mappedStem,
                Ext = mappedExt,
                Folder = item.Folder,
                Parent = item.Parent,
                IsDir = item.IsDir,
                Size = item.Size,
                Created = item.Created,
                Modified = item.Modified,
                Accessed = item.Accessed,
                Depth = item.Depth,
                Selected = item.Selected,
                Exif = item.Exif,
                Id3 = item.Id3,
                Props = item.Props
            };
        }

        var stem = item.Stem;
        var ext = item.Ext;
        var warning = "";

        if (rules.RegexEnabled && !string.IsNullOrEmpty(rules.RegexPattern))
        {
            try
            {
                var regexReplace = Tokens.Expand(rules.RegexReplace, item, rules, sequence, preserveRegexGroups: true);
                (stem, ext) = ApplyToParts(stem, ext, rules.RegexApplyTo,
                    value => RegexSub(rules.RegexPattern, regexReplace, value));
            }
            catch (ArgumentException ex)
            {
                warning = "Invalid regex: " + ex.Message;
            }
        }

        if (rules.NameEnabled)
        {
            if (rules.NameMode == "fixed")
                stem = Tokens.Expand(rules.NameFixed, item, rules, sequence);
            else if (rules.NameMode == "remove")
                stem = "";

            if (rules.ExtMode == "fixed")
            {
                var fixedExt = Tokens.Expand(rules.ExtFixed, item, rules, sequence).TrimStart('.');
                ext = string.IsNullOrEmpty(fixedExt) ? "" : "." + fixedExt;
            }
            else if (rules.ExtMode == "remove") ext = "";
            else if (rules.ExtMode == "lower") ext = ext.ToLowerInvariant();
            else if (rules.ExtMode == "upper") ext = ext.ToUpperInvariant();
        }

        if (rules.ReplaceEnabled && !string.IsNullOrEmpty(rules.Find))
        {
            var replaceWith = Tokens.Expand(rules.ReplaceWith, item, rules, sequence);
            (stem, ext) = ApplyToParts(stem, ext, rules.ReplaceApplyTo,
                value => FindReplace(value, rules.Find, replaceWith, rules.ReplaceAll, rules.ReplaceCaseSensitive));
        }

        if (rules.SwapEnabled && !string.IsNullOrEmpty(rules.SwapSeparator))
            (stem, ext) = ApplyToParts(stem, ext, "name", value => SwapAround(value, rules.SwapSeparator));

        if (rules.CaseMode != "same")
            (stem, ext) = ApplyToParts(stem, ext, rules.CaseApplyTo, value => ApplyCase(value, rules.CaseMode));

        if (rules.StripAccents)
            (stem, ext) = ApplyToParts(stem, ext, rules.CaseApplyTo, StripAccents);

        if (rules.RemoveEnabled)
            stem = ApplyRemove(stem, rules);

        if (rules.MoveEnabled)
            stem = ApplyMove(stem, rules.MoveFrom, rules.MoveCount, rules.MoveTo, rules.MoveCopy);

        if (rules.AddEnabled)
        {
            var prefix = Tokens.Expand(rules.Prefix, item, rules, sequence);
            var suffix = Tokens.Expand(rules.Suffix, item, rules, sequence);
            var inserted = Tokens.Expand(rules.Insert, item, rules, sequence);
            if (!string.IsNullOrEmpty(prefix)) stem = prefix + stem;
            if (rules.InsertAt > 0 && !string.IsNullOrEmpty(inserted))
                stem = InsertAt(stem, inserted, rules.InsertAt);
            if (!string.IsNullOrEmpty(suffix)) stem += suffix;
        }

        if (rules.FolderEnabled && !string.IsNullOrEmpty(item.Folder))
            stem = ApplyPosition(stem, item.Folder, rules.FolderMode, rules.FolderSeparator);

        if (rules.DateEnabled)
        {
            var stamp = Tokens.Expand("{date}", item, rules, sequence);
            stem = ApplyPosition(stem, stamp, rules.DatePosition, rules.DateSeparator);
        }

        if (rules.RenumberEnabled)
            stem = ReplaceLastNumber(stem, sequence.ToString().PadLeft(Math.Max(rules.NumberPad, 1), '0'));

        if (rules.NumberingEnabled)
        {
            var number = sequence.ToString().PadLeft(Math.Max(rules.NumberPad, 1), '0');
            stem = ApplyPosition(stem, number, rules.NumberPosition, rules.NumberSeparator, rules.NumberInsertAt);
        }

        var newName = Names.JoinName(stem, ext);

        if (rules.JsEnabled && !string.IsNullOrWhiteSpace(rules.JsCode))
        {
            var (jsStem, jsExt) = Names.SplitName(newName);
            var (result, jsError) = JsRules.Run(rules.JsCode, new Dictionary<string, object?>
            {
                ["name"] = jsStem,
                ["ext"] = jsExt.TrimStart('.'),
                ["newName"] = newName,
                ["index"] = sequence,
                ["folder"] = item.Folder,
                ["size"] = item.Size,
                ["path"] = item.Path,
                ["isDir"] = item.IsDir,
                ["created"] = item.Created?.ToString("O"),
                ["modified"] = item.Modified?.ToString("O"),
                ["accessed"] = item.Accessed?.ToString("O"),
                ["exif"] = item.Exif,
                ["id3"] = item.Id3,
                ["props"] = item.Props
            });
            if (!string.IsNullOrEmpty(jsError)) warning = jsError;
            else if (!string.IsNullOrEmpty(result)) newName = result;
        }

        newName = newName.Replace('\\', '_').Replace('/', '_');
        if (string.IsNullOrWhiteSpace(newName) || newName is "." or "..")
            return (newName, string.IsNullOrEmpty(warning) ? "New name is empty" : warning);
        if (rules.WindowsSafe)
            newName = Names.WindowsSafeName(newName);
        return (newName, warning);
    }

    public static List<PreviewRow> BuildPreview(IReadOnlyList<FileItem> items, Rules rules, string sortColumn = "name", bool sortDesc = false)
    {
        var ordered = SortItems(items, sortColumn, sortDesc);
        var sequences = AssignSequences(ordered, rules);
        var rows = new List<PreviewRow>();
        var proposed = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in ordered)
        {
            if (!item.Selected)
            {
                rows.Add(MakeRow(item, item.Name, item.Path, selected: false, changed: false, "skipped"));
                continue;
            }

            var seq = sequences.GetValueOrDefault(item.Path, rules.NumberStart);
            var (newName, warning) = ApplyRules(item, rules, seq);
            var newPath = DestinationPath(item, newName, rules, seq);
            var status = "ok";
            if (string.IsNullOrEmpty(newName) || newName is "." or "..")
            {
                status = "invalid";
                if (string.IsNullOrEmpty(warning)) warning = "New name is empty";
            }
            else if (newName == item.Name && rules.Operation == "rename")
                status = "unchanged";

            var row = MakeRow(item, newName, newPath, true, newName != item.Name || rules.Operation != "rename", status, warning);
            rows.Add(row);
            if (status is "ok" or "unchanged")
            {
                var key = KeyFor(newPath);
                if (!proposed.TryGetValue(key, out var list))
                    proposed[key] = list = [];
                list.Add(item.Path);
            }
        }

        var conflicts = proposed.Where(kv => kv.Value.Count > 1).Select(kv => kv.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var marker = KeyFor(row.NewPath);
            if (row.Selected && row.Status != "invalid" && conflicts.Contains(marker))
            {
                row.Status = "conflict";
                row.Warning = "Duplicate new name";
            }
            else if (row.Selected && row.Status == "ok")
            {
                if (File.Exists(row.NewPath) || Directory.Exists(row.NewPath))
                {
                    var same = string.Equals(Path.GetFullPath(row.NewPath), Path.GetFullPath(row.Path), StringComparison.OrdinalIgnoreCase);
                    if (!same && !ordered.Any(other => other.Selected && string.Equals(other.Path, row.NewPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        row.Status = "exists";
                        row.Warning = "Target already exists";
                    }
                }
            }
        }

        ApplyCollisionPolicy(rows, rules);
        return rows;
    }

    static void ApplyCollisionPolicy(List<PreviewRow> rows, Rules rules)
    {
        var policy = string.IsNullOrWhiteSpace(rules.CollisionPolicy) ? "fail" : rules.CollisionPolicy;
        if (policy is not "skip" and not "append") return;

        if (policy == "skip")
        {
            foreach (var row in rows)
            {
                if (row.Selected && row.Status is "conflict" or "exists")
                {
                    row.Status = "skipped";
                    row.Changed = false;
                    if (string.IsNullOrEmpty(row.Warning))
                        row.Warning = "Skipped due to name collision";
                }
            }
            return;
        }

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (row.Selected && row.Status is "ok" or "unchanged")
                taken.Add(KeyFor(row.NewPath));
        }

        foreach (var row in rows)
        {
            if (!row.Selected || row.Status is not ("conflict" or "exists")) continue;
            var desired = row.NewPath;
            var unique = NextFreePath(desired, row.Path, taken);
            if (unique is null)
            {
                row.Status = "skipped";
                row.Changed = false;
                row.Warning = "Could not find a free name";
                continue;
            }
            row.NewPath = unique;
            row.NewName = Path.GetFileName(unique);
            row.Status = "ok";
            row.Changed = !string.Equals(unique, row.Path, StringComparison.OrdinalIgnoreCase);
            row.Warning = string.Equals(KeyFor(unique), KeyFor(desired), StringComparison.OrdinalIgnoreCase)
                ? ""
                : "Appended a number to avoid a collision";
            taken.Add(KeyFor(unique));
        }
    }

    static string? NextFreePath(string desired, string originalPath, HashSet<string> taken)
    {
        bool IsFree(string path)
        {
            var key = KeyFor(path);
            if (taken.Contains(key)) return false;
            try
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    var same = string.Equals(Path.GetFullPath(path), Path.GetFullPath(originalPath), StringComparison.OrdinalIgnoreCase);
                    return same;
                }
            }
            catch { /* treat as free */ }
            return true;
        }

        if (IsFree(desired)) return desired;
        var dir = Path.GetDirectoryName(desired) ?? "";
        var (stem, ext) = Names.SplitName(Path.GetFileName(desired));
        for (var i = 1; i <= 9999; i++)
        {
            var candidate = Path.Combine(dir, Names.JoinName($"{stem}_{i:000}", ext));
            if (IsFree(candidate)) return candidate;
        }
        return null;
    }

    static PreviewRow MakeRow(FileItem item, string newName, string newPath, bool selected, bool changed, string status, string warning = "") =>
        new()
        {
            Path = item.Path,
            OldName = item.Name,
            NewName = newName,
            NewPath = newPath,
            IsDir = item.IsDir,
            Size = item.Size,
            Created = Iso(item.Created),
            Modified = Iso(item.Modified),
            Accessed = Iso(item.Accessed),
            Selected = selected,
            Changed = changed,
            Status = status,
            Warning = warning,
            Folder = item.Folder,
            Ext = item.Ext
        };

    static string KeyFor(string path)
    {
        try
        {
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                return Path.GetFullPath(path);
        }
        catch { /* ignore */ }
        return path;
    }

    static string? Iso(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm:ss");

    static List<FileItem> SortItems(IReadOnlyList<FileItem> items, string column, bool desc)
    {
        IOrderedEnumerable<FileItem> ordered = column switch
        {
            "size" => items.OrderBy(i => i.Size),
            "modified" => items.OrderBy(i => i.Modified ?? DateTime.MinValue),
            "created" => items.OrderBy(i => i.Created ?? DateTime.MinValue),
            "path" => items.OrderBy(i => i.Path, Natural),
            "ext" => items.OrderBy(i => i.Ext, StringComparer.OrdinalIgnoreCase),
            "folder" => items.OrderBy(i => i.Folder, Natural),
            _ => items.OrderBy(i => i.Name, Natural)
        };
        var list = ordered.ToList();
        if (desc) list.Reverse();
        return list;
    }

    static Dictionary<string, int> AssignSequences(IEnumerable<FileItem> items, Rules rules)
    {
        var sequences = new Dictionary<string, int>();
        if (rules.NumberResetPerFolder)
        {
            var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items.Where(i => i.Selected))
            {
                if (!counters.TryGetValue(item.Parent, out var current))
                    current = rules.NumberStart;
                sequences[item.Path] = current;
                counters[item.Parent] = current + rules.NumberIncrement;
            }
            return sequences;
        }
        var n = rules.NumberStart;
        foreach (var item in items.Where(i => i.Selected))
        {
            sequences[item.Path] = n;
            n += rules.NumberIncrement;
        }
        return sequences;
    }

    static string DestinationPath(FileItem item, string newName, Rules rules, int sequence)
    {
        if (rules.Operation == "rename" || string.IsNullOrWhiteSpace(rules.DestDir))
            return Path.Combine(item.Parent, newName);
        var destRoot = Tokens.Expand(rules.DestDir, item, rules, sequence);
        if (!Path.IsPathRooted(destRoot))
            destRoot = Path.Combine(item.Parent, destRoot);
        return Path.Combine(destRoot, newName);
    }

    static (string Stem, string Ext) ApplyToParts(string stem, string ext, string applyTo, Func<string, string> transform)
    {
        var extBody = ext.StartsWith('.') ? ext[1..] : ext;
        var prefix = ext.StartsWith('.') && extBody.Length > 0 ? "." : "";
        return applyTo switch
        {
            "ext" => (stem, string.IsNullOrEmpty(ext) ? ext : prefix + transform(extBody)),
            "both" => (transform(stem), string.IsNullOrEmpty(ext) ? ext : prefix + transform(extBody)),
            "full" => Names.SplitName(transform(Names.JoinName(stem, ext))),
            _ => (transform(stem), ext)
        };
    }

    static string ApplyCase(string value, string mode) => mode switch
    {
        "lower" => value.ToLowerInvariant(),
        "upper" => value.ToUpperInvariant(),
        "title" => TitleCaseManual(value),
        "sentence" => SentenceCase(value),
        "toggle" => new string(value.Select(c => char.IsLetter(c) ? (char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)) : c).ToArray()),
        "kebab" => ToKebab(value),
        "slug" => ToSlug(value),
        _ => value
    };

    internal static string ToKebab(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var sb = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsWhiteSpace(c) || c is '_' or '.' or '/' or '\\')
            {
                AppendHyphen(sb);
                continue;
            }
            if (c == '-')
            {
                AppendHyphen(sb);
                continue;
            }
            if (char.IsUpper(c) && sb.Length > 0 && sb[^1] != '-')
            {
                var prev = value[i - 1];
                var nextLower = i + 1 < value.Length && char.IsLower(value[i + 1]);
                if (!char.IsUpper(prev) || nextLower)
                    sb.Append('-');
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        return TrimHyphens(sb);
    }

    internal static string ToSlug(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        string normalized;
        try { normalized = value.Normalize(NormalizationForm.FormD); }
        catch { normalized = value; }
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            var lower = char.ToLowerInvariant(c);
            if (char.IsLetterOrDigit(lower))
                sb.Append(lower);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        return TrimHyphens(sb);
    }

    static void AppendHyphen(StringBuilder sb)
    {
        if (sb.Length > 0 && sb[^1] != '-')
            sb.Append('-');
    }

    static string TrimHyphens(StringBuilder sb)
    {
        var start = 0;
        var end = sb.Length;
        while (start < end && sb[start] == '-') start++;
        while (end > start && sb[end - 1] == '-') end--;
        return start == 0 && end == sb.Length ? sb.ToString() : sb.ToString(start, end - start);
    }

    internal static string StripAccents(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        string normalized;
        try { normalized = value.Normalize(NormalizationForm.FormD); }
        catch { return value; }
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(c);
        }
        try { return sb.ToString().Normalize(NormalizationForm.FormC); }
        catch { return sb.ToString(); }
    }

    internal static string SwapAround(string value, string separator)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(separator)) return value;
        var index = value.IndexOf(separator, StringComparison.Ordinal);
        if (index < 0) return value;
        var left = value[..index];
        var right = value[(index + separator.Length)..];
        if (left.Length == 0 || right.Length == 0) return value;
        return right + separator + left;
    }

    internal static string ReplaceLastNumber(string value, string number)
    {
        if (string.IsNullOrEmpty(value) || !LastNumberRe.IsMatch(value)) return value;
        return LastNumberRe.Replace(value, number, 1);
    }

    static string TitleCaseManual(string value)
    {
        var parts = TitleSplit.Split(value);
        var output = new List<string>();
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part) || Regex.IsMatch(part, @"^[_\-\s]+$"))
                output.Add(part);
            else
                output.Add(char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant());
        }
        return string.Concat(output);
    }

    static string SentenceCase(string value)
    {
        var lowered = value.ToLowerInvariant();
        for (var i = 0; i < lowered.Length; i++)
        {
            if (char.IsLetter(lowered[i]))
                return lowered[..i] + char.ToUpperInvariant(lowered[i]) + lowered[(i + 1)..];
        }
        return lowered;
    }

    static string RegexSub(string pattern, string repl, string value)
    {
        var converted = Regex.Replace(repl, @"\$(\d+)", "$${$1}");
        converted = Regex.Replace(converted, @"\\g<(\d+)>", "$${$1}");
        return Regex.Replace(value, pattern, converted);
    }

    static string FindReplace(string value, string find, string repl, bool replaceAll, bool caseSensitive)
    {
        if (caseSensitive)
            return replaceAll ? value.Replace(find, repl) : ReplaceFirst(value, find, repl, StringComparison.Ordinal);
        var options = RegexOptions.IgnoreCase;
        var count = replaceAll ? int.MaxValue : 1;
        return new Regex(Regex.Escape(find), options).Replace(value, _ => repl, count);
    }

    static string ReplaceFirst(string value, string find, string repl, StringComparison comparison)
    {
        var index = value.IndexOf(find, comparison);
        if (index < 0) return value;
        return value[..index] + repl + value[(index + find.Length)..];
    }

    static string ApplyRemove(string value, Rules rules)
    {
        var chars = value.ToCharArray().ToList();
        if (rules.RemoveFirstN > 0)
            chars = chars.Skip(rules.RemoveFirstN).ToList();
        if (rules.RemoveLastN > 0)
            chars = rules.RemoveLastN >= chars.Count ? [] : chars.SkipLast(rules.RemoveLastN).ToList();
        if (rules.RemoveFrom > 0)
        {
            var start = rules.RemoveFrom - 1;
            var end = rules.RemoveTo > 0 ? rules.RemoveTo : start + 1;
            if (start < chars.Count)
            {
                var takeEnd = Math.Min(end, chars.Count);
                chars = chars.Take(start).Concat(chars.Skip(takeEnd)).ToList();
            }
        }
        var result = new string(chars.ToArray());
        if (!string.IsNullOrEmpty(rules.RemoveChars))
            result = new string(result.Where(c => !rules.RemoveChars.Contains(c)).ToArray());
        if (!string.IsNullOrEmpty(rules.RemoveWords))
        {
            foreach (var word in rules.RemoveWords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                result = Regex.Replace(result, Regex.Escape(word), "", RegexOptions.IgnoreCase);
        }
        if (rules.RemoveFirstWords > 0 || rules.RemoveLastWords > 0)
            result = RemoveWordsByCount(result, rules.RemoveFirstWords, rules.RemoveLastWords);
        if (rules.RemoveDigits) result = DigitRe.Replace(result, "");
        if (rules.RemoveSymbols) result = SymbolRe.Replace(result, "");
        if (rules.RemoveLetters) result = LetterRe.Replace(result, "");
        if (rules.CollapseSpaces) result = Regex.Replace(result, @"\s+", " ");
        if (rules.RemoveTrim) result = result.Trim();
        return result;
    }

    static string RemoveWordsByCount(string value, int first, int last)
    {
        var bits = WordSplit.Split(value).Where(p => p.Length > 0).ToList();
        var words = bits.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (first > 0) words = words.Skip(first).ToList();
        if (last > 0) words = last >= words.Count ? [] : words.SkipLast(last).ToList();
        return string.Join(" ", words);
    }

    static string ApplyMove(string value, int fromPos, int count, int toPos, bool copy)
    {
        if (fromPos <= 0 || count <= 0) return value;
        var start = fromPos - 1;
        if (start >= value.Length) return value;
        var take = Math.Min(count, value.Length - start);
        var chunk = value.Substring(start, take);
        var remaining = copy ? value : value[..start] + value[(start + take)..];
        var insertAt = Math.Clamp(toPos - 1, 0, remaining.Length);
        return remaining[..insertAt] + chunk + remaining[insertAt..];
    }

    static string InsertAt(string value, string text, int position)
    {
        if (string.IsNullOrEmpty(text)) return value;
        if (position <= 0) return value + text;
        var index = Math.Clamp(position - 1, 0, value.Length);
        return value[..index] + text + value[index..];
    }

    static string ApplyPosition(string value, string extra, string position, string separator, int insertPos = 0)
    {
        if (string.IsNullOrEmpty(extra)) return value;
        return position switch
        {
            "prefix" => string.IsNullOrEmpty(separator) ? extra + value : extra + separator + value,
            "suffix" => string.IsNullOrEmpty(separator) ? value + extra : value + separator + extra,
            "replace" => extra,
            "insert" => InsertAt(value, insertPos > 1 && !string.IsNullOrEmpty(separator) ? separator + extra : extra, insertPos),
            _ => value
        };
    }

    /// <summary>
    /// Windows filenames are case-insensitive, so mapping keys must match regardless of the dictionary comparer.
    /// Exact match still wins when a case-sensitive dictionary contains both "Photo.jpg" and "photo.jpg".
    /// </summary>
    internal static bool TryLookupMapping(IReadOnlyDictionary<string, string> mapping, FileItem item, out string mapped) =>
        TryLookupMappingKey(mapping, item.Name, out mapped) || TryLookupMappingKey(mapping, item.Path, out mapped);

    static bool TryLookupMappingKey(IReadOnlyDictionary<string, string> mapping, string key, out string mapped)
    {
        if (mapping.TryGetValue(key, out mapped!) && !string.IsNullOrWhiteSpace(mapped))
            return true;
        foreach (var pair in mapping)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(pair.Value))
            {
                mapped = pair.Value;
                return true;
            }
        }
        mapped = "";
        return false;
    }

    internal static DateTime? ParseExifDate(FileItem item)
    {
        if (!item.Exif.TryGetValue("date", out var raw) && !item.Exif.TryGetValue("DateTimeOriginal", out raw))
            return null;
        string[] formats =
        [
            "yyyy:MM:dd HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd",
            "dd/MM/yyyy HH:mm:ss",
            "dd/MM/yyyy",
            "MM/dd/yyyy HH:mm:ss",
            "MM/dd/yyyy"
        ];
        foreach (var fmt in formats)
        {
            if (DateTime.TryParseExact(raw, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed;
        }
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback))
            return fallback;
        return null;
    }

}
