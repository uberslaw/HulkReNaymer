using HulkReNaymer;

namespace HulkReNaymer.Tests;

public class GitMoveTests
{
    [Fact]
    public void TrackedRename_UsesGitMv_AndUndoRestores()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-gitmv-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "data");
        var folder = Path.Combine(root, "docs");
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("HULKRENAYMER_DATA", data);
        File.WriteAllText(Path.Combine(folder, "final report.docx"), "payload");
        try
        {
            GitTestRepo.Init(root, "main");
            Assert.True(GitOps.IsTracked(Path.Combine(folder, "final report.docx")));

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
            Assert.Equal(1, result.Renamed);
            var renamed = Path.Combine(folder, "PROJECT123_Final_Report.docx");
            Assert.True(File.Exists(renamed));
            Assert.Equal("payload", File.ReadAllText(renamed));

            var status = GitTestRepo.Status(root);
            Assert.Contains("PROJECT123_Final_Report.docx", status);
            Assert.DoesNotContain("??", status.Split('\n').FirstOrDefault(l => l.Contains("PROJECT123_Final_Report.docx")) ?? "??");
            Assert.Contains("R", status);

            var undone = ApplyService.UndoLast();
            Assert.Equal(1, undone.Renamed);
            Assert.True(File.Exists(Path.Combine(folder, "final report.docx")));
            Assert.False(File.Exists(renamed));
            Assert.Equal("payload", File.ReadAllText(Path.Combine(folder, "final report.docx")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void UntrackedFile_FallsBackToFileMove_AndUndoWorks()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-ungit-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("HULKRENAYMER_DATA", data);
        File.WriteAllText(Path.Combine(root, "alpha.txt"), "a");
        try
        {
            GitTestRepo.Init(root, "main");
            var extra = Path.Combine(root, "loose.txt");
            File.WriteAllText(extra, "loose");
            Assert.False(GitOps.IsTracked(extra));

            var item = MetadataReader.Describe(extra);
            var rules = new Rules { AddEnabled = true, Prefix = "X_" };
            var result = ApplyService.Commit(RenameEngine.BuildPreview([item], rules), rules);
            Assert.Equal(1, result.Renamed);
            Assert.True(File.Exists(Path.Combine(root, "X_loose.txt")));
            Assert.False(GitOps.IsTracked(Path.Combine(root, "X_loose.txt")));

            var undone = ApplyService.UndoLast();
            Assert.Equal(1, undone.Renamed);
            Assert.True(File.Exists(extra));
            Assert.Equal("loose", File.ReadAllText(extra));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void GitMv_SwapTrackedNames_PreservesContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-gitswap-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("HULKRENAYMER_DATA", data);
        File.WriteAllText(Path.Combine(root, "alpha.txt"), "A");
        File.WriteAllText(Path.Combine(root, "beta.txt"), "B");
        try
        {
            GitTestRepo.Init(root, "main");
            var (items, _) = Scanner.Scan(root);
            var rules = new Rules { Mapping = new Dictionary<string, string> { ["alpha.txt"] = "beta.txt", ["beta.txt"] = "alpha.txt" } };
            var result = ApplyService.Commit(RenameEngine.BuildPreview(items, rules), rules);
            Assert.Equal(2, result.Renamed);
            Assert.Equal("B", File.ReadAllText(Path.Combine(root, "alpha.txt")));
            Assert.Equal("A", File.ReadAllText(Path.Combine(root, "beta.txt")));
            Assert.True(GitOps.IsTracked(Path.Combine(root, "alpha.txt")));
            Assert.True(GitOps.IsTracked(Path.Combine(root, "beta.txt")));

            var undone = ApplyService.UndoLast();
            Assert.Equal(2, undone.Renamed);
            Assert.Equal("A", File.ReadAllText(Path.Combine(root, "alpha.txt")));
            Assert.Equal("B", File.ReadAllText(Path.Combine(root, "beta.txt")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }
}
