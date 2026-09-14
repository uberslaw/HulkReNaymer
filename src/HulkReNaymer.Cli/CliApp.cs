using System.Text;
using HulkReNaymer;

namespace HulkReNaymer.Cli;

public static class CliApp
{
    static readonly string[] Commands = ["preview", "apply", "undo", "seed", "help"];
    static readonly HashSet<string> ValuedOptions = new(StringComparer.Ordinal)
    {
        "-w", "--wildcard", "--find", "--replace", "--prefix", "--suffix",
        "--case", "--preset", "--favorite", "--collision", "--from-list",
        "--regex", "--regex-replace"
    };

    public static int Run(string[] args, TextWriter? stdout = null, TextWriter? stderr = null)
    {
        stdout ??= Console.Out;
        stderr ??= Console.Error;
        if (!TryParse(args, out var opt, out var error))
        {
            stderr.WriteLine(error);
            stderr.WriteLine();
            stderr.WriteLine(HelpText());
            return 2;
        }
        if (opt.Help || opt.Command == "help")
        {
            stdout.WriteLine(HelpText());
            return 0;
        }

        try
        {
            return opt.Command switch
            {
                "undo" => Undo(stdout),
                "seed" => Seed(opt, stdout),
                "apply" => PreviewOrApply(opt, apply: !opt.DryRun, stdout, stderr),
                _ => PreviewOrApply(opt, apply: false, stdout, stderr)
            };
        }
        catch (Exception ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
    }

    static int PreviewOrApply(CliOptions opt, bool apply, TextWriter stdout, TextWriter stderr)
    {
        var (items, warning) = CollectItems(opt);
        if (!string.IsNullOrEmpty(warning))
            stderr.WriteLine(warning);
        if (items.Count == 0)
        {
            stderr.WriteLine("No files or folders matched.");
            return 1;
        }

        var rules = BuildRules(opt);
        var rows = RenameEngine.BuildPreview(items, rules);
        PrintTable(rows, stdout);
        var wouldChange = rows.Count(r => r.Changed && r.Status == "ok");
        stdout.WriteLine();
        stdout.WriteLine($"{rows.Count} listed  ·  {wouldChange} would change  ·  collision={rules.CollisionPolicy}");

        if (!apply)
            return 0;

        var result = ApplyService.Commit(rows, rules);
        stdout.WriteLine(result.Message);
        foreach (var fail in result.Failed)
            stderr.WriteLine($"FAILED {fail.Path}: {fail.Error}");
        return result.Failed.Count > 0 ? 1 : 0;
    }

    static int Undo(TextWriter stdout)
    {
        var result = ApplyService.UndoLast();
        stdout.WriteLine(result.Message);
        return result.Failed.Count > 0 ? 1 : 0;
    }

    static int Seed(CliOptions opt, TextWriter stdout)
    {
        var dest = opt.Paths.Count > 0 ? opt.Paths[0] : null;
        var path = DemoSeeder.Seed(dest);
        stdout.WriteLine("Demo files written to " + path);
        return 0;
    }

    static (List<FileItem> Items, string Warning) CollectItems(CliOptions opt)
    {
        var paths = opt.Paths.Count > 0 ? opt.Paths : [Directory.GetCurrentDirectory()];
        var files = new List<string>();
        var dirs = new List<string>();
        foreach (var raw in paths)
        {
            string full;
            try { full = Path.GetFullPath(raw); }
            catch { continue; }
            if (Directory.Exists(full) && !File.Exists(full))
                dirs.Add(full);
            else if (File.Exists(full))
                files.Add(full);
        }

        if (files.Count == 0 && dirs.Count == 1)
            return Scanner.Scan(dirs[0], opt.Recurse, includeFiles: true, includeFolders: opt.Folders, wildcard: opt.Wildcard, includeHidden: opt.Hidden);

        var items = new List<FileItem>();
        var warning = "";
        if (files.Count > 0)
        {
            var (found, _) = Scanner.FromPaths(files, includeFiles: true, includeFolders: false, includeHidden: opt.Hidden);
            items.AddRange(found);
        }
        foreach (var dir in dirs)
        {
            if (opt.Recurse || opt.ScanDirs)
            {
                var (found, warn) = Scanner.Scan(dir, opt.Recurse, includeFiles: true, includeFolders: opt.Folders, wildcard: opt.Wildcard, includeHidden: opt.Hidden);
                items.AddRange(found);
                if (!string.IsNullOrEmpty(warn)) warning = warn;
            }
            else if (opt.Folders)
            {
                var (found, _) = Scanner.FromPaths([dir], includeFiles: false, includeFolders: true, includeHidden: opt.Hidden);
                items.AddRange(found);
            }
        }
        return (items, warning);
    }

    static Rules BuildRules(CliOptions opt)
    {
        Rules rules;
        if (!string.IsNullOrWhiteSpace(opt.Favorite))
        {
            var fav = FavoritesStore.Load().FirstOrDefault(f => string.Equals(f.Name, opt.Favorite, StringComparison.OrdinalIgnoreCase));
            rules = fav?.Rules.Clone() ?? new Rules();
        }
        else if (!string.IsNullOrWhiteSpace(opt.Preset))
            rules = RulePresets.Create(opt.Preset);
        else
            rules = new Rules();

        if (!string.IsNullOrEmpty(opt.Find))
        {
            rules.ReplaceEnabled = true;
            rules.Find = opt.Find;
            rules.ReplaceWith = opt.Replace;
        }
        if (!string.IsNullOrEmpty(opt.Prefix) || !string.IsNullOrEmpty(opt.Suffix))
        {
            rules.AddEnabled = true;
            if (!string.IsNullOrEmpty(opt.Prefix)) rules.Prefix = opt.Prefix;
            if (!string.IsNullOrEmpty(opt.Suffix)) rules.Suffix = opt.Suffix;
        }
        if (!string.IsNullOrEmpty(opt.Case) && opt.Case != "same")
            rules.CaseMode = opt.Case;
        if (!string.IsNullOrEmpty(opt.Regex))
        {
            rules.RegexEnabled = true;
            rules.RegexPattern = opt.Regex;
            rules.RegexReplace = opt.RegexReplace;
        }
        if (!string.IsNullOrEmpty(opt.FromList) && File.Exists(opt.FromList))
            rules.Mapping = FavoritesStore.ParseMapping(File.ReadAllText(opt.FromList));
        if (opt.CollisionSpecified)
            rules.CollisionPolicy = opt.Collision;
        return rules;
    }

    static void PrintTable(IReadOnlyList<PreviewRow> rows, TextWriter stdout)
    {
        var widthOld = Math.Max(8, rows.Count == 0 ? 8 : rows.Max(r => r.OldName.Length));
        var widthNew = Math.Max(8, rows.Count == 0 ? 8 : rows.Max(r => r.NewName.Length));
        stdout.WriteLine($"{"STATUS",-10} {Pad( "OLD NAME", widthOld)}  {Pad("NEW NAME", widthNew)}");
        stdout.WriteLine(new string('-', widthOld + widthNew + 14));
        foreach (var row in rows)
        {
            var flag = row.Changed ? "*" : " ";
            var warn = string.IsNullOrEmpty(row.Warning) ? "" : "  " + row.Warning;
            stdout.WriteLine($"{row.Status,-10} {Pad(row.OldName, widthOld)}  {Pad(row.NewName, widthNew)} {flag}{warn}");
        }
    }

    static string Pad(string value, int width) =>
        value.Length >= width ? value : value + new string(' ', width - value.Length);

    internal static bool TryParse(string[] args, out CliOptions opt, out string error)
    {
        opt = new CliOptions();
        error = "";
        var i = 0;
        if (args.Length > 0 && Commands.Contains(args[0], StringComparer.OrdinalIgnoreCase))
        {
            opt.Command = args[0].ToLowerInvariant();
            i = 1;
        }

        while (i < args.Length)
        {
            var arg = args[i];
            if (arg == "--")
            {
                opt.Paths.AddRange(args[(i + 1)..]);
                break;
            }
            if (arg is "-h" or "--help")
            {
                opt.Help = true;
                i++;
                continue;
            }
            if (arg is "-r" or "--recurse") { opt.Recurse = true; i++; continue; }
            if (arg is "--folders") { opt.Folders = true; i++; continue; }
            if (arg is "--hidden") { opt.Hidden = true; i++; continue; }
            if (arg is "--apply") { opt.Command = "apply"; opt.DryRun = false; i++; continue; }
            if (arg is "--dry-run" or "--preview") { opt.DryRun = true; if (opt.Command == "apply") opt.Command = "preview"; i++; continue; }
            if (arg is "--scan-dirs") { opt.ScanDirs = true; i++; continue; }
            if (!arg.StartsWith('-'))
            {
                opt.Paths.Add(arg);
                i++;
                continue;
            }

            string? value = null;
            var eq = arg.IndexOf('=');
            string name = eq > 0 ? arg[..eq] : arg;
            if (!ValuedOptions.Contains(name))
            {
                error = "Unknown option: " + name;
                return false;
            }
            if (eq > 0)
                value = arg[(eq + 1)..];
            else
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                {
                    error = "Missing value for " + name;
                    return false;
                }
                value = args[++i];
            }

            switch (name)
            {
                case "-w":
                case "--wildcard": opt.Wildcard = value ?? "*"; break;
                case "--find": opt.Find = value ?? ""; break;
                case "--replace": opt.Replace = value ?? ""; break;
                case "--prefix": opt.Prefix = value ?? ""; break;
                case "--suffix": opt.Suffix = value ?? ""; break;
                case "--case": opt.Case = value ?? "same"; break;
                case "--preset": opt.Preset = value ?? ""; break;
                case "--favorite": opt.Favorite = value ?? ""; break;
                case "--collision":
                    opt.Collision = (value ?? "fail").ToLowerInvariant();
                    opt.CollisionSpecified = true;
                    break;
                case "--from-list": opt.FromList = value ?? ""; break;
                case "--regex": opt.Regex = value ?? ""; break;
                case "--regex-replace": opt.RegexReplace = value ?? ""; break;
                default:
                    error = "Unknown option: " + name;
                    return false;
            }
            i++;
        }

        if (opt.Command == "apply" && !opt.DryRun)
            opt.Command = "apply";
        if (opt.Collision is not ("fail" or "skip" or "append"))
        {
            error = "Collision policy must be fail, skip, or append.";
            return false;
        }
        return true;
    }

    public static string HelpText()
    {
        var presets = string.Join(", ", RulePresets.Names.Where(n => n.Length > 0));
        var sb = new StringBuilder();
        sb.AppendLine("HulkReNaymer.Cli — preview and apply bulk rename rules.");
        sb.AppendLine();
        sb.AppendLine("Usage:");
        sb.AppendLine("  dotnet run --project src/HulkReNaymer.Cli -- [command] [paths...] [options]");
        sb.AppendLine("  HulkReNaymer.Cli.exe [command] [paths...] [options]");
        sb.AppendLine();
        sb.AppendLine("Commands: preview (default) | apply | undo | seed");
        sb.AppendLine();
        sb.AppendLine("Options:");
        sb.AppendLine("  -r, --recurse           Scan folders recursively");
        sb.AppendLine("  -w, --wildcard PATTERN  Filter names (default *)");
        sb.AppendLine("      --folders           Include folders as rename targets");
        sb.AppendLine("      --hidden            Include hidden / dot files");
        sb.AppendLine("      --find TEXT         Find / replace (use --replace)");
        sb.AppendLine("      --replace TEXT");
        sb.AppendLine("      --prefix TEXT       Add prefix");
        sb.AppendLine("      --suffix TEXT       Add suffix");
        sb.AppendLine("      --case MODE         same|lower|upper|title|sentence|toggle|kebab|slug");
        sb.AppendLine("      --preset NAME       Built-in preset");
        sb.AppendLine("      --favorite NAME     Load a saved favourite");
        sb.AppendLine("      --collision MODE    fail|skip|append (default fail)");
        sb.AppendLine("      --from-list FILE    Old|New mapping list");
        sb.AppendLine("      --regex PATTERN     Regex match");
        sb.AppendLine("      --regex-replace TEXT");
        sb.AppendLine("      --apply             Commit (same as apply command)");
        sb.AppendLine("      --dry-run           Preview only (default)");
        sb.AppendLine("  -h, --help");
        sb.AppendLine();
        sb.AppendLine("Presets: " + presets);
        sb.AppendLine("Tokens: {name} {ext} {folder} {n} {date} {yyyy} {git.branch} {hash:8} {exif.date} {id3.artist}");
        return sb.ToString();
    }
}

public sealed class CliOptions
{
    public string Command { get; set; } = "preview";
    public List<string> Paths { get; } = [];
    public bool Recurse { get; set; }
    public bool Folders { get; set; }
    public bool Hidden { get; set; }
    public bool ScanDirs { get; set; }
    public bool DryRun { get; set; }
    public bool Help { get; set; }
    public string Wildcard { get; set; } = "*";
    public string Find { get; set; } = "";
    public string Replace { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Suffix { get; set; } = "";
    public string Case { get; set; } = "same";
    public string Preset { get; set; } = "";
    public string Favorite { get; set; } = "";
    public string Collision { get; set; } = "fail";
    public bool CollisionSpecified { get; set; }
    public string FromList { get; set; } = "";
    public string Regex { get; set; } = "";
    public string RegexReplace { get; set; } = "";
}
