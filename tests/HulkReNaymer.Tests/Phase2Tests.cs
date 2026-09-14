using System.Security.Cryptography;
using System.Text;
using HulkReNaymer;
using HulkReNaymer.Cli;

namespace HulkReNaymer.Tests;

public class Phase2Tests
{
    static FileItem Item(string name, string folder = "project") =>
        new()
        {
            Path = $"/tmp/{folder}/{name}",
            Name = name,
            Stem = Names.SplitName(name).Stem,
            Ext = Names.SplitName(name).Ext,
            Folder = folder,
            Parent = $"/tmp/{folder}"
        };

    [Fact]
    public void KebabAndSlug_CaseModes()
    {
        Assert.Equal("hello-world.txt", RenameEngine.ApplyRules(Item("Hello World.txt"), new Rules { CaseMode = "kebab" }, 1).NewName);
        Assert.Equal("final-report.docx", RenameEngine.ApplyRules(Item("FinalReport.docx"), new Rules { CaseMode = "kebab" }, 1).NewName);
        Assert.Equal("xml-parser.cs", RenameEngine.ApplyRules(Item("XMLParser.cs"), new Rules { CaseMode = "kebab" }, 1).NewName);
        Assert.Equal("snake-case-name.md", RenameEngine.ApplyRules(Item("snake_case_name.md"), new Rules { CaseMode = "kebab" }, 1).NewName);
        Assert.Equal("cafe-au-lait.txt", RenameEngine.ApplyRules(Item("Café au lait!.txt"), new Rules { CaseMode = "slug" }, 1).NewName);
        Assert.Equal("final-report-2.pdf", RenameEngine.ApplyRules(Item("Final Report (2).pdf"), new Rules { CaseMode = "slug" }, 1).NewName);
    }

    [Fact]
    public void Presets_KebabAndSlug()
    {
        Assert.Contains("kebab-case", RulePresets.Names);
        Assert.Contains("slug", RulePresets.Names);
        Assert.Equal("kebab", RulePresets.Create("kebab-case").CaseMode);
        Assert.Equal("slug", RulePresets.Create("slug").CaseMode);
        Assert.Equal("hello-world.txt", RenameEngine.ApplyRules(Item("Hello World.txt"), RulePresets.Create("kebab-case"), 1).NewName);
        Assert.Equal("office-notes.txt", RenameEngine.ApplyRules(Item("Office Notes!.txt"), RulePresets.Create("slug"), 1).NewName);
        Assert.Equal("PROJECT123_Final_Report.docx",
            RenameEngine.ApplyRules(Item("final report.docx"), RulePresets.Create("Project documents"), 1).NewName);
    }

    [Fact]
    public void HashToken_UsesSha256Prefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "hello.txt");
        try
        {
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("hello"));
            var item = MetadataReader.Describe(path);
            var expected = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes("hello"))).ToLowerInvariant();
            Assert.Equal(expected[..8], Tokens.Expand("{hash:8}", item, new Rules(), 1));
            Assert.Equal(expected[..8], Tokens.Expand("{hash}", item, new Rules(), 1));
            Assert.Equal(expected[..12], Tokens.Expand("{hash:12}", item, new Rules(), 1));
            Assert.Equal(expected[..8] + "-hello.txt",
                RenameEngine.ApplyRules(item, new Rules { AddEnabled = true, Prefix = "{hash:8}-" }, 1).NewName);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void GitBranchToken_ReadsCurrentBranch()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-branch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "notes.txt");
        File.WriteAllText(path, "x");
        try
        {
            GitTestRepo.Init(root, "gamma-lab");
            var item = MetadataReader.Describe(path);
            Assert.Equal("gamma-lab", Tokens.Expand("{git.branch}", item, new Rules(), 1));
            Assert.Equal("gamma-lab_notes.txt",
                RenameEngine.ApplyRules(item, new Rules { AddEnabled = true, Prefix = "{git.branch}_" }, 1).NewName);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void GitBranchToken_EmptyOutsideRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-nongit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "lone.txt");
        File.WriteAllText(path, "x");
        try
        {
            var item = MetadataReader.Describe(path);
            Assert.Equal("", Tokens.Expand("{git.branch}", item, new Rules(), 1));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Cli_PreviewFindReplaceAndHelp()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "QLD Office Report.docx"), "x");
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var code = CliApp.Run(["preview", root, "--find", "Office", "--replace", "Regional"], stdout, stderr);
            Assert.Equal(0, code);
            Assert.Contains("QLD Regional Report.docx", stdout.ToString());
            Assert.Contains("would change", stdout.ToString());

            var help = new StringWriter();
            Assert.Equal(0, CliApp.Run(["--help"], help, new StringWriter()));
            Assert.Contains("HulkReNaymer.Cli", help.ToString());
            Assert.Contains("kebab-case", help.ToString());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Cli_ApplyPresetKebab_ThenUndo()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-cli-apply-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("HULKRENAYMER_DATA", data);
        File.WriteAllText(Path.Combine(root, "Hello World.txt"), "ok");
        try
        {
            var stdout = new StringWriter();
            var code = CliApp.Run(["apply", root, "--preset", "kebab-case"], stdout, new StringWriter());
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(root, "hello-world.txt")));
            Assert.False(File.Exists(Path.Combine(root, "Hello World.txt")));

            var undone = new StringWriter();
            Assert.Equal(0, CliApp.Run(["undo"], undone, new StringWriter()));
            Assert.True(File.Exists(Path.Combine(root, "Hello World.txt")));
            Assert.Equal("ok", File.ReadAllText(Path.Combine(root, "Hello World.txt")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Cli_CollisionSkip_AndFavorite()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-cli-fav-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("HULKRENAYMER_DATA", data);
        File.WriteAllText(Path.Combine(root, "source.txt"), "src");
        File.WriteAllText(Path.Combine(root, "taken.txt"), "taken");
        FavoritesStore.Save("smash", new Rules { NameEnabled = true, NameMode = "fixed", NameFixed = "taken", CollisionPolicy = "skip" });
        try
        {
            var stdout = new StringWriter();
            var code = CliApp.Run(["preview", Path.Combine(root, "source.txt"), "--favorite", "smash"], stdout, new StringWriter());
            Assert.Equal(0, code);
            Assert.Contains("skipped", stdout.ToString());

            var appendOut = new StringWriter();
            Assert.Equal(0, CliApp.Run(["preview", Path.Combine(root, "source.txt"), "--find", "source", "--replace", "taken", "--collision", "append"], appendOut, new StringWriter()));
            Assert.Contains("taken_001.txt", appendOut.ToString());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Cli_UnknownOption_IsUsageError()
    {
        var stderr = new StringWriter();
        var code = CliApp.Run(["--explode"], new StringWriter(), stderr);
        Assert.Equal(2, code);
        Assert.Contains("Unknown option", stderr.ToString());
    }
}

internal static class GitTestRepo
{
    public static void Init(string root, string branch)
    {
        Run(root, "init", "-b", branch);
        Run(root, "config", "user.email", "hulk@example.test");
        Run(root, "config", "user.name", "Hulk Tester");
        Run(root, "config", "commit.gpgsign", "false");
        Run(root, "add", "-A");
        Run(root, "commit", "-m", "init");
    }

    public static string Status(string root) => Run(root, "status", "--porcelain");

    public static string Run(string root, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(root);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        psi.Environment.Remove("GIT_DIR");
        psi.Environment.Remove("GIT_WORK_TREE");
        psi.Environment.Remove("GIT_INDEX_FILE");
        using var proc = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("git missing");
        var output = proc.StandardOutput.ReadToEnd();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {err}");
        return output;
    }
}
