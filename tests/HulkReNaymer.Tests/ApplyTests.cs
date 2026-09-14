using HulkReNaymer;

namespace HulkReNaymer.Tests;

public class ApplyTests
{
    [Fact]
    public void ParseMapping()
    {
        var mapping = FavoritesStore.ParseMapping("OldName01.pdf|NewName01.pdf\nOldName02.pdf,NewName02.pdf\n# comment\n");
        Assert.Equal("NewName01.pdf", mapping["OldName01.pdf"]);
        Assert.Equal("NewName01.pdf", mapping["oldname01.pdf"]);
        Assert.Equal("NewName02.pdf", mapping["OldName02.pdf"]);
    }

    [Fact]
    public void RenameAndUndo()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-test-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "data");
        var folder = Path.Combine(root, "docs");
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("HULKRENAYMER_DATA", data);
        try
        {
            File.WriteAllText(Path.Combine(folder, "final report.docx"), "a");
            File.WriteAllText(Path.Combine(folder, "cost plan.xlsx"), "b");
            var (items, _) = Scanner.Scan(folder);
            var rules = new Rules
            {
                CaseMode = "title",
                ReplaceEnabled = true,
                Find = " ",
                ReplaceWith = "_",
                AddEnabled = true,
                Prefix = "PROJECT123_"
            };
            var rows = RenameEngine.BuildPreview(items, rules);
            var result = ApplyService.Commit(rows, rules);
            Assert.Equal(2, result.Renamed);
            Assert.True(File.Exists(Path.Combine(folder, "PROJECT123_Final_Report.docx")));
            Assert.True(File.Exists(Path.Combine(folder, "PROJECT123_Cost_Plan.xlsx")));
            var undone = ApplyService.UndoLast();
            Assert.Equal(2, undone.Renamed);
            Assert.True(File.Exists(Path.Combine(folder, "final report.docx")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void SwapNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-swap-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("HULKRENAYMER_DATA", Path.Combine(root, "data"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "alpha.txt"), "a");
        File.WriteAllText(Path.Combine(root, "beta.txt"), "b");
        try
        {
            var (items, _) = Scanner.Scan(root);
            var rules = new Rules { Mapping = new Dictionary<string, string> { ["alpha.txt"] = "beta.txt", ["beta.txt"] = "alpha.txt" } };
            var result = ApplyService.Commit(RenameEngine.BuildPreview(items, rules), rules);
            Assert.Equal(2, result.Renamed);
            Assert.Equal("b", File.ReadAllText(Path.Combine(root, "alpha.txt")));
            Assert.Equal("a", File.ReadAllText(Path.Combine(root, "beta.txt")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CollisionPolicy_SkipLeavesExistingTarget_AppendAddsNumber()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-collide-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("HULKRENAYMER_DATA", data);
        File.WriteAllText(Path.Combine(root, "source.txt"), "src");
        File.WriteAllText(Path.Combine(root, "taken.txt"), "taken");
        try
        {
            FileItem Item(string name) => MetadataReader.Describe(Path.Combine(root, name));
            var skipRules = new Rules
            {
                NameEnabled = true,
                NameMode = "fixed",
                NameFixed = "taken",
                CollisionPolicy = "skip"
            };
            var skipped = RenameEngine.BuildPreview([Item("source.txt")], skipRules);
            Assert.Equal("skipped", skipped[0].Status);
            var skipResult = ApplyService.Commit(skipped, skipRules);
            Assert.Equal(0, skipResult.Renamed);
            Assert.True(File.Exists(Path.Combine(root, "source.txt")));

            var appendRules = new Rules
            {
                NameEnabled = true,
                NameMode = "fixed",
                NameFixed = "taken",
                CollisionPolicy = "append"
            };
            var appended = RenameEngine.BuildPreview([Item("source.txt")], appendRules);
            Assert.Equal("taken_001.txt", appended[0].NewName);
            Assert.Equal("ok", appended[0].Status);
            var result = ApplyService.Commit(appended, appendRules);
            Assert.Equal(1, result.Renamed);
            Assert.True(File.Exists(Path.Combine(root, "taken_001.txt")));
            Assert.Equal("src", File.ReadAllText(Path.Combine(root, "taken_001.txt")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Scanner_FromPaths_KeepsExplicitFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-paths-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var keep = Path.Combine(root, "keep.txt");
        var extra = Path.Combine(root, "extra.txt");
        File.WriteAllText(keep, "k");
        File.WriteAllText(extra, "e");
        try
        {
            var (items, warning) = Scanner.FromPaths([keep, extra + ".missing"]);
            Assert.Equal("", warning);
            Assert.Single(items);
            Assert.Equal("keep.txt", items[0].Name);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void SeedAndExif()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-seed-" + Guid.NewGuid().ToString("N"));
        try
        {
            var demo = DemoSeeder.Seed(root);
            Assert.Equal(3, Directory.GetFiles(Path.Combine(demo, "photos"), "DSC*.jpg").Length);
            var (items, _) = Scanner.Scan(Path.Combine(demo, "photos"), wildcard: "DSC*.jpg");
            Assert.Contains(items, item => item.Exif.ContainsKey("date"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }
}
