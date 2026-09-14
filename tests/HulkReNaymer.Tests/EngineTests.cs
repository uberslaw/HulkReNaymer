using HulkReNaymer;

namespace HulkReNaymer.Tests;

public class EngineTests
{
    static FileItem Item(string name, string folder = "project", Dictionary<string, string>? exif = null, Dictionary<string, string>? id3 = null) =>
        new()
        {
            Path = $"/tmp/{folder}/{name}",
            Name = name,
            Stem = Names.SplitName(name).Stem,
            Ext = Names.SplitName(name).Ext,
            Folder = folder,
            Parent = $"/tmp/{folder}",
            Size = 100,
            Created = new DateTime(2026, 9, 14, 9, 0, 0),
            Modified = new DateTime(2026, 9, 14, 10, 0, 0),
            Accessed = new DateTime(2026, 9, 14, 11, 0, 0),
            Exif = exif ?? [],
            Id3 = id3 ?? []
        };

    [Fact]
    public void PrefixSuffixAndReplace()
    {
        Assert.Equal("Project-A_Report.pdf", RenameEngine.ApplyRules(Item("Report.pdf"), new Rules { AddEnabled = true, Prefix = "Project-A_" }, 1).NewName);
        Assert.Equal("AssetRegister_Approved.xlsx", RenameEngine.ApplyRules(Item("AssetRegister.xlsx"), new Rules { AddEnabled = true, Suffix = "_Approved" }, 1).NewName);
        Assert.Equal("QLD Regional Report.docx", RenameEngine.ApplyRules(Item("QLD Office Report.docx"), new Rules { ReplaceEnabled = true, Find = "Office", ReplaceWith = "Regional" }, 1).NewName);
    }

    [Fact]
    public void RemoveAndInsert()
    {
        Assert.Equal("2026_0001.jpg", RenameEngine.ApplyRules(Item("IMG_2026_0001.jpg"), new Rules { RemoveEnabled = true, RemoveFirstN = 4 }, 1).NewName);
        Assert.Equal("Project-Report.pdf", RenameEngine.ApplyRules(Item("ProjectReport.pdf"), new Rules { AddEnabled = true, Insert = "-", InsertAt = 8 }, 1).NewName);
    }

    [Fact]
    public void TitleCase()
    {
        Assert.Equal("Brisbane Office Asset Register.xlsx",
            RenameEngine.ApplyRules(Item("brisbane OFFICE asset REGISTER.xlsx"), new Rules { CaseMode = "title" }, 1).NewName);
    }

    [Fact]
    public void ProjectDocumentsExample()
    {
        var rules = new Rules
        {
            CaseMode = "title",
            ReplaceEnabled = true,
            Find = " ",
            ReplaceWith = "_",
            AddEnabled = true,
            Prefix = "PROJECT123_"
        };
        Assert.Equal("PROJECT123_Final_Report.docx", RenameEngine.ApplyRules(Item("final report.docx"), rules, 1).NewName);
        Assert.Equal("PROJECT123_Cost_Plan.xlsx", RenameEngine.ApplyRules(Item("cost plan.xlsx"), rules, 1).NewName);
        Assert.Equal("PROJECT123_Site_Photos.zip", RenameEngine.ApplyRules(Item("site photos.zip"), rules, 1).NewName);
    }

    [Fact]
    public void StripExportPrefix()
    {
        var rules = new Rules { ReplaceEnabled = true, Find = "EXPORT_20260914_", ReplaceWith = "" };
        Assert.Equal("Asset_Report_001.csv", RenameEngine.ApplyRules(Item("EXPORT_20260914_Asset_Report_001.csv"), rules, 1).NewName);
    }

    [Fact]
    public void FolderPrefixAndExtension()
    {
        Assert.Equal("Brisbane_AssetList.xlsx",
            RenameEngine.ApplyRules(Item("AssetList.xlsx", "Brisbane"), new Rules { FolderEnabled = true, FolderMode = "prefix", FolderSeparator = "_" }, 1).NewName);
        Assert.Equal("FILE01.jpeg",
            RenameEngine.ApplyRules(Item("FILE01.JPEG"), new Rules { NameEnabled = true, ExtMode = "lower" }, 1).NewName);
    }

    [Fact]
    public void NumberingAndDates()
    {
        var items = Enumerable.Range(1, 3).Select(i => Item($"Photo{i}.jpg")).ToList();
        var rows = RenameEngine.BuildPreview(items, new Rules { NumberingEnabled = true, NumberStart = 1, NumberPad = 3, NumberSeparator = "_", NumberPosition = "suffix" });
        Assert.Equal(new[] { "Photo1_001.jpg", "Photo2_002.jpg", "Photo3_003.jpg" }, rows.Select(r => r.NewName));
        Assert.Equal("2026-09-14_MeetingNotes.docx",
            RenameEngine.ApplyRules(Item("MeetingNotes.docx"), new Rules { DateEnabled = true, DateSource = "modified", DateFormat = "yyyy-MM-dd", DatePosition = "prefix" }, 1).NewName);
    }

    [Fact]
    public void PhotoExifWorkflow()
    {
        var item = Item("DSC0001.jpg", exif: new Dictionary<string, string>
        {
            ["date"] = "2026:09:14 09:30:00",
            ["width"] = "320",
            ["height"] = "240"
        });
        var rules = new Rules
        {
            NameEnabled = true,
            NameMode = "remove",
            AddEnabled = true,
            Suffix = "Site-Inspection",
            DateEnabled = true,
            DateSource = "exif",
            DateFormat = "yyyy-MM-dd",
            NumberingEnabled = true,
            NumberPad = 3
        };
        Assert.Equal("2026-09-14_Site-Inspection_001.jpg", RenameEngine.ApplyRules(item, rules, 1).NewName);
    }

    [Fact]
    public void MappingAndRegex()
    {
        Assert.Equal("AU123456_HP-ZBook.jpg",
            RenameEngine.ApplyRules(Item("IMG001.jpg"), new Rules { Mapping = new Dictionary<string, string> { ["IMG001.jpg"] = "AU123456_HP-ZBook.jpg" } }, 1).NewName);
        Assert.Equal("Client Name - Invoice 2026.pdf",
            RenameEngine.ApplyRules(Item("Invoice 2026 - Client Name.pdf"), new Rules
            {
                RegexEnabled = true,
                RegexPattern = @"^(Invoice \d+) - (.+)$",
                RegexReplace = "$2 - $1"
            }, 1).NewName);
    }

    [Fact]
    public void ConflictsAndBlankNames()
    {
        var rows = RenameEngine.BuildPreview([Item("a.txt"), Item("b.txt")], new Rules { NameEnabled = true, NameMode = "fixed", NameFixed = "same" });
        Assert.All(rows, r => Assert.Equal("conflict", r.Status));
        var blank = RenameEngine.BuildPreview([Item("keep.txt")], new Rules { NameEnabled = true, NameMode = "remove", ExtMode = "remove" });
        Assert.Equal("invalid", blank[0].Status);
    }

    [Fact]
    public void Id3Tokens()
    {
        var item = Item("Track01.mp3", id3: new Dictionary<string, string>
        {
            ["artist"] = "Hulk Smash Band",
            ["title"] = "Street Inspection",
            ["album"] = "Gamma Rays"
        });
        Assert.Equal("Hulk Smash Band - Street Inspection.mp3",
            RenameEngine.ApplyRules(item, new Rules { NameEnabled = true, NameMode = "fixed", NameFixed = "{id3.artist} - {id3.title}" }, 1).NewName);
    }

    [Fact]
    public void JavaScriptRule()
    {
        var result = RenameEngine.ApplyRules(Item("Track01.txt"), new Rules { JsEnabled = true, JsCode = "newName = name + '_smash.' + ext;" }, 1);
        Assert.Equal("Track01_smash.txt", result.NewName);
    }
}
