using System.Text.Json;

namespace HulkReNaymer;

public static class ApplyService
{
    public static string DataDir
    {
        get
        {
            var path = Environment.GetEnvironmentVariable("HULKRENAYMER_DATA")
                       ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HulkReNaymer");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    record UndoOp(string Operation, string Source, string Dest, bool IsDir);
    record UndoBatch(string Id, string CreatedAt, List<UndoOp> Ops);

    static string UndoPath => Path.Combine(DataDir, "undo.json");
    static string LogPath => Path.Combine(DataDir, "hulkrenaymer.log");

    public static CommitResult Commit(IReadOnlyList<PreviewRow> rows, Rules rules)
    {
        var actionable = rows.Where(r => r.Selected && r.Status == "ok" && r.Changed).ToList();
        if (actionable.Count == 0)
            return new CommitResult { Message = "Nothing to rename." };

        var conflicts = rows.Where(r => r.Status is "conflict" or "invalid" or "exists").ToList();
        if (conflicts.Count > 0)
        {
            return new CommitResult
            {
                Message = "Fix conflicts before renaming.",
                Failed = conflicts.Select(r => new FailedItem { Path = r.Path, Error = string.IsNullOrEmpty(r.Warning) ? r.Status : r.Warning }).ToList()
            };
        }

        if (rules.Operation is "copy" or "move" && !string.IsNullOrWhiteSpace(rules.DestDir) && Path.IsPathRooted(rules.DestDir))
            Directory.CreateDirectory(rules.DestDir);

        var failed = new List<FailedItem>();
        var undoOps = new List<UndoOp>();

        if (rules.Operation == "copy")
        {
            foreach (var row in actionable)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(row.NewPath)!);
                    if (row.IsDir) CopyDirectory(row.Path, row.NewPath);
                    else File.Copy(row.Path, row.NewPath, overwrite: false);
                    ApplyAttributes(row.NewPath, rules);
                    undoOps.Add(new UndoOp("copy", row.Path, row.NewPath, row.IsDir));
                    AppendLog($"COPY  {row.Path} -> {row.NewPath}");
                }
                catch (Exception ex)
                {
                    failed.Add(new FailedItem { Path = row.Path, Error = ex.Message });
                }
            }
            var batch = SaveBatch(undoOps);
            return new CommitResult { Renamed = undoOps.Count, Failed = failed, UndoId = batch.Id, Message = $"Copied {undoOps.Count} item(s)." };
        }

        var temps = new List<(PreviewRow Row, string Temp)>();
        foreach (var row in actionable)
        {
            try
            {
                var temp = UniqueTemp(Path.GetDirectoryName(row.Path)!, Path.GetExtension(row.Path));
                if (row.IsDir) Directory.Move(row.Path, temp);
                else File.Move(row.Path, temp);
                temps.Add((row, temp));
            }
            catch (Exception ex)
            {
                failed.Add(new FailedItem { Path = row.Path, Error = ex.Message });
            }
        }

        var renamed = 0;
        foreach (var (row, temp) in temps)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(row.NewPath)!);
                if (row.IsDir) Directory.Move(temp, row.NewPath);
                else File.Move(temp, row.NewPath);
                ApplyAttributes(row.NewPath, rules);
                undoOps.Add(new UndoOp(rules.Operation, row.Path, row.NewPath, row.IsDir));
                AppendLog($"{rules.Operation.ToUpperInvariant()}  {row.Path} -> {row.NewPath}");
                renamed++;
            }
            catch (Exception ex)
            {
                try
                {
                    if (row.IsDir) Directory.Move(temp, row.Path);
                    else File.Move(temp, row.Path);
                }
                catch { /* keep temp */ }
                failed.Add(new FailedItem { Path = row.Path, Error = ex.Message });
            }
        }

        string? undoId = null;
        if (undoOps.Count > 0)
            undoId = SaveBatch(undoOps).Id;
        var verb = rules.Operation == "rename" ? "Renamed" : "Moved";
        return new CommitResult { Renamed = renamed, Failed = failed, UndoId = undoId, Message = $"{verb} {renamed} item(s)." };
    }

    public static CommitResult UndoLast()
    {
        var batches = LoadUndo();
        if (batches.Count == 0)
            return new CommitResult { Message = "Nothing to undo." };
        var batch = batches[^1];
        batches.RemoveAt(batches.Count - 1);
        var undone = 0;
        var failed = new List<FailedItem>();
        foreach (var op in batch.Ops.AsEnumerable().Reverse())
        {
            try
            {
                if (op.Operation == "copy")
                {
                    if (op.IsDir && Directory.Exists(op.Dest)) Directory.Delete(op.Dest, true);
                    else if (File.Exists(op.Dest)) File.Delete(op.Dest);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(op.Source)!);
                    if (op.IsDir) Directory.Move(op.Dest, op.Source);
                    else File.Move(op.Dest, op.Source);
                }
                undone++;
                AppendLog($"UNDO  {op.Dest} -> {op.Source}");
            }
            catch (Exception ex)
            {
                failed.Add(new FailedItem { Path = op.Dest, Error = ex.Message });
            }
        }
        SaveUndo(batches);
        return new CommitResult { Renamed = undone, Failed = failed, Message = $"Undid {undone} item(s)." };
    }

    public static bool HasUndo => LoadUndo().Count > 0;

    static void ApplyAttributes(string path, Rules rules)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        if (rules.SetReadonly)
        {
            try
            {
                var attrs = File.GetAttributes(path);
                attrs = rules.ReadonlyValue ? attrs | FileAttributes.ReadOnly : attrs & ~FileAttributes.ReadOnly;
                File.SetAttributes(path, attrs);
            }
            catch { /* ignore */ }
        }
        if (rules.SetHidden)
        {
            try
            {
                var attrs = File.GetAttributes(path);
                attrs = rules.HiddenValue ? attrs | FileAttributes.Hidden : attrs & ~FileAttributes.Hidden;
                File.SetAttributes(path, attrs);
            }
            catch { /* ignore */ }
        }
        if (rules.SetTimestamps)
        {
            try
            {
                var modified = ParseIso(rules.TsModified) ?? File.GetLastWriteTime(path);
                var accessed = ParseIso(rules.TsAccessed) ?? File.GetLastAccessTime(path);
                File.SetLastWriteTime(path, modified);
                File.SetLastAccessTime(path, accessed);
            }
            catch { /* ignore */ }
        }
    }

    static DateTime? ParseIso(string value) =>
        DateTime.TryParse(value, out var parsed) ? parsed : null;

    static string UniqueTemp(string parent, string ext)
    {
        var temp = Path.Combine(parent, ".hulkrenaymer-" + Guid.NewGuid().ToString("N") + ext);
        return temp;
    }

    static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    static List<UndoBatch> LoadUndo()
    {
        if (!File.Exists(UndoPath)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<UndoBatch>>(File.ReadAllText(UndoPath)) ?? [];
        }
        catch { return []; }
    }

    static void SaveUndo(List<UndoBatch> batches)
    {
        var keep = batches.TakeLast(20).ToList();
        File.WriteAllText(UndoPath, JsonSerializer.Serialize(keep, new JsonSerializerOptions { WriteIndented = true }));
    }

    static UndoBatch SaveBatch(List<UndoOp> ops)
    {
        var batch = new UndoBatch(DateTime.Now.ToString("yyyyMMdd-HHmmss"), DateTime.Now.ToString("O"), ops);
        var batches = LoadUndo();
        batches.Add(batch);
        SaveUndo(batches);
        return batch;
    }

    public static void AppendLog(string message)
    {
        File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
    }

    public static string ReadLog()
    {
        if (!File.Exists(LogPath)) return "";
        var lines = File.ReadAllLines(LogPath);
        return string.Join(Environment.NewLine, lines.TakeLast(400));
    }
}
