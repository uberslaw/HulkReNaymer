using HulkReNaymer;

namespace HulkReNaymer.Tests;

public class Phase3Tests
{
    static FileItem Item(string name, string folder = "project", Dictionary<string, string>? exif = null) =>
        new()
        {
            Path = $"/tmp/{folder}/{name}",
            Name = name,
            Stem = Names.SplitName(name).Stem,
            Ext = Names.SplitName(name).Ext,
            Folder = folder,
            Parent = $"/tmp/{folder}",
            Exif = exif ?? []
        };

    [Fact]
    public void TokenCatalog_InsertsAtCaret_AndListsPickerTokens()
    {
        var text = TokenCatalog.Insert("PROJECT_", 8, "{hash:8}", out var caret);
        Assert.Equal("PROJECT_{hash:8}", text);
        Assert.Equal(16, caret);
        Assert.Contains(TokenCatalog.All, t => t.Token == "{git.branch}");
        Assert.Contains(TokenCatalog.All, t => t.Token == "{hash:8}");
        Assert.Contains(TokenCatalog.All, t => t.Token == "{exif.make}");
        Assert.Contains(TokenCatalog.All, t => t.Token == "{exif.model}");
        Assert.Contains(TokenCatalog.All, t => t.Token == "{exif.camera}");
        Assert.Contains("PrefixBox", TokenCatalog.TargetBoxNames);
        Assert.Contains("DestDirBox", TokenCatalog.TargetBoxNames);
        Assert.Contains("RegexReplace", TokenCatalog.TargetBoxNames);
    }

    [Fact]
    public void ExtraExifTokens_FromSeededJpeg()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-exif3-" + Guid.NewGuid().ToString("N"));
        try
        {
            var demo = DemoSeeder.Seed(root);
            var item = MetadataReader.Describe(Path.Combine(demo, "photos", "DSC0001.jpg"));
            Assert.Equal("HulkCam", item.Exif["make"]);
            Assert.Equal("Gamma", item.Exif["model"]);
            Assert.Equal("HulkCam Gamma", Tokens.Expand("{exif.camera}", item, new Rules(), 1));
            Assert.Equal("HulkCam", Tokens.Expand("{exif.make}", item, new Rules(), 1));
            Assert.Equal("Gamma", Tokens.Expand("{exif.model}", item, new Rules(), 1));
            Assert.Equal("HulkCam-Gamma.jpg",
                RenameEngine.ApplyRules(item, new Rules { NameEnabled = true, NameMode = "fixed", NameFixed = "{exif.make}-{exif.model}" }, 1).NewName);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Accents_StripDiacriticsButKeepCaseAndPunctuation()
    {
        Assert.Equal("Cafe au lait!.txt",
            RenameEngine.ApplyRules(Item("Café au lait!.txt"), new Rules { StripAccents = true }, 1).NewName);
        Assert.Equal("naive resume.pdf",
            RenameEngine.ApplyRules(Item("naïve résumé.pdf"), new Rules { StripAccents = true }, 1).NewName);
    }

    [Fact]
    public void Swap_AroundSeparator()
    {
        Assert.Equal("Client Name - Invoice 2026.pdf",
            RenameEngine.ApplyRules(Item("Invoice 2026 - Client Name.pdf"), new Rules { SwapEnabled = true, SwapSeparator = " - " }, 1).NewName);
        Assert.Equal("title_artist.mp3",
            RenameEngine.ApplyRules(Item("artist_title.mp3"), new Rules { SwapEnabled = true, SwapSeparator = "_" }, 1).NewName);
        Assert.Equal("NoSeparator.txt",
            RenameEngine.ApplyRules(Item("NoSeparator.txt"), new Rules { SwapEnabled = true, SwapSeparator = " - " }, 1).NewName);
    }

    [Fact]
    public void Renumber_RestylesLastNumberGroup()
    {
        var items = new[] { Item("Photo2.jpg"), Item("Photo10.jpg") };
        var rows = RenameEngine.BuildPreview(items, new Rules { RenumberEnabled = true, NumberStart = 1, NumberPad = 3 });
        Assert.Equal(new[] { "Photo001.jpg", "Photo002.jpg" }, rows.Select(r => r.NewName));
        Assert.Equal("IMG_2026_007.jpg",
            RenameEngine.ApplyRules(Item("IMG_2026_0001.jpg"), new Rules { RenumberEnabled = true, NumberPad = 3 }, 7).NewName);
        Assert.Equal("plain.txt",
            RenameEngine.ApplyRules(Item("plain.txt"), new Rules { RenumberEnabled = true, NumberPad = 3 }, 1).NewName);
    }

    [Fact]
    public void RuleOrder_SwapThenAccentsThenRenumber()
    {
        var result = RenameEngine.ApplyRules(Item("Café - Report 9.docx"), new Rules
        {
            SwapEnabled = true,
            SwapSeparator = " - ",
            StripAccents = true,
            RenumberEnabled = true,
            NumberPad = 2
        }, 3);
        Assert.Equal("Report 03 - Cafe.docx", result.NewName);
    }
}
