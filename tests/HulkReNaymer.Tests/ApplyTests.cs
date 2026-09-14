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
