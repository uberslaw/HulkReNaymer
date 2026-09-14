using HulkReNaymer;

namespace HulkReNaymer.Tests;

/// <summary>
/// Documents and verifies assumptions the app makes about the OS, metadata libraries, and JS engine.
/// </summary>
public class AssumptionTests
{
    [Fact]
    public void FileInfo_Exists_IsFalse_ForDirectories()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hulk-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var info = new FileInfo(dir);
            Assert.True(Directory.Exists(dir));
            Assert.False(info.Exists, "FileInfo.Exists is assumed false for directories so Describe() can tell files from folders.");
            var item = MetadataReader.Describe(dir);
            Assert.True(item.IsDir);
            Assert.Equal(dir, item.Path);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public void SeededJpeg_ExifDate_IsParseableAndUsedInPhotoPreset()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-exif-" + Guid.NewGuid().ToString("N"));
        try
        {
            var demo = DemoSeeder.Seed(root);
            var photo = Path.Combine(demo, "photos", "DSC0001.jpg");
            var item = MetadataReader.Describe(photo);
            Assert.Equal("2026:09:14 09:30:00", item.Exif["date"]);
            Assert.Equal("320", item.Exif["width"]);

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
            var (newName, warning) = RenameEngine.ApplyRules(item, rules, 1);
            Assert.True(string.IsNullOrEmpty(warning), warning);
            Assert.Equal("2026-09-14_Site-Inspection_001.jpg", newName);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void SeededMp3_HasId3ArtistTitle()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-id3-" + Guid.NewGuid().ToString("N"));
        try
        {
            var demo = DemoSeeder.Seed(root);
            var mp3 = Path.Combine(demo, "music", "Track01.mp3");
            var item = MetadataReader.Describe(mp3);
            Assert.Equal("Hulk Smash Band", item.Id3.GetValueOrDefault("artist"));
            Assert.Equal("Street Inspection", item.Id3.GetValueOrDefault("title"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void JavaScript_CanReadExifAndId3DotProperties()
    {
        var item = new FileItem
        {
            Path = "/tmp/x.jpg",
            Name = "x.jpg",
            Stem = "x",
            Ext = ".jpg",
            Folder = "tmp",
            Parent = "/tmp",
            Exif = new Dictionary<string, string> { ["date"] = "2026:09:14 09:30:00" },
            Id3 = new Dictionary<string, string> { ["artist"] = "Hulk" }
        };
        var rules = new Rules
        {
            JsEnabled = true,
            JsCode = "newName = (id3.artist || 'noartist') + '_' + (exif.date ? 'hasdate' : 'nodate') + '.jpg';"
        };
        var (newName, warning) = RenameEngine.ApplyRules(item, rules, 1);
        Assert.True(string.IsNullOrEmpty(warning), warning);
        Assert.Equal("Hulk_hasdate.jpg", newName);
    }

    [Fact]
    public void PythonStrftimeTokens_AreConverted()
    {
        Assert.Equal("yyyy-MM-dd", Names.ConvertDateFormat("%Y-%m-%d"));
        var item = new FileItem
        {
            Path = "/tmp/MeetingNotes.docx",
            Name = "MeetingNotes.docx",
            Stem = "MeetingNotes",
            Ext = ".docx",
            Folder = "tmp",
            Parent = "/tmp",
            Modified = new DateTime(2026, 9, 14, 10, 0, 0)
        };
        var (newName, _) = RenameEngine.ApplyRules(item, new Rules
        {
            DateEnabled = true,
            DateSource = "modified",
            DateFormat = "%Y-%m-%d",
            DatePosition = "prefix"
        }, 1);
        Assert.Equal("2026-09-14_MeetingNotes.docx", newName);
    }

    [Fact]
    public void Scanner_ListsFilesInsideUserBinFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-bin-" + Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(root, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "payload.txt"), "x");
        try
        {
            var (inside, _) = Scanner.Scan(bin);
            Assert.Contains(inside, i => i.Name == "payload.txt");

            var (recursive, _) = Scanner.Scan(root, recurse: true);
            Assert.Contains(recursive, i => i.Name == "payload.txt");

            Assert.Contains(Scanner.ListChildren(root), child => child.Name == "bin");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scanner_DoesNotWalkIntoGitMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        File.WriteAllText(Path.Combine(root, ".git", "HEAD"), "ref: refs/heads/main");
        File.WriteAllText(Path.Combine(root, "readme.txt"), "ok");
        try
        {
            var (items, _) = Scanner.Scan(root, recurse: true, includeHidden: true);
            Assert.Contains(items, i => i.Name == "readme.txt");
            Assert.DoesNotContain(items, i => i.Name == "HEAD");
            Assert.DoesNotContain(Scanner.ListChildren(root), child => child.Name == ".git");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SplitName_MatchesDotNetAndDotfiles()
    {
        Assert.Equal(("FILE01", ".JPEG"), Names.SplitName("FILE01.JPEG"));
        Assert.Equal((".gitignore", ""), Names.SplitName(".gitignore"));
        Assert.Equal(("final report", ".docx"), Names.SplitName("final report.docx"));
        Assert.Equal(("archive.tar", ".gz"), Names.SplitName("archive.tar.gz"));
    }

    [Fact]
    public void WindowsSafe_RewritesReservedAndIllegal()
    {
        Assert.Equal("_CON.txt", Names.WindowsSafeName("CON.txt"));
        Assert.Equal("a_b.txt", Names.WindowsSafeName("a<b.txt"));
        Assert.Equal("ends", Names.WindowsSafeName("ends."));
    }

    [Fact]
    public void Regex_SupportsDollarAndPythonGroupSyntax()
    {
        var item = new FileItem
        {
            Path = "/tmp/Invoice 2026 - Client Name.pdf",
            Name = "Invoice 2026 - Client Name.pdf",
            Stem = "Invoice 2026 - Client Name",
            Ext = ".pdf",
            Folder = "tmp",
            Parent = "/tmp"
        };
        var dollar = RenameEngine.ApplyRules(item, new Rules { RegexEnabled = true, RegexPattern = @"^(Invoice \d+) - (.+)$", RegexReplace = "$2 - $1" }, 1).NewName;
        var python = RenameEngine.ApplyRules(item, new Rules { RegexEnabled = true, RegexPattern = @"^(Invoice \d+) - (.+)$", RegexReplace = @"\g<2> - \g<1>" }, 1).NewName;
        Assert.Equal("Client Name - Invoice 2026.pdf", dollar);
        Assert.Equal("Client Name - Invoice 2026.pdf", python);
    }

    [Fact]
    public void Mapping_IsCaseInsensitive_ForWindowsFilenames()
    {
        var item = new FileItem
        {
            Path = @"C:\photos\IMG001.jpg",
            Name = "IMG001.jpg",
            Stem = "IMG001",
            Ext = ".jpg",
            Folder = "photos",
            Parent = @"C:\photos"
        };
        var rules = new Rules { Mapping = new Dictionary<string, string> { ["img001.jpg"] = "Renamed.jpg" } };
        Assert.Equal("Renamed.jpg", RenameEngine.ApplyRules(item, rules, 1).NewName);
    }

    [Fact]
    public void Mapping_ParseAndClone_StayCaseInsensitive()
    {
        var parsed = FavoritesStore.ParseMapping("img001.jpg|Renamed.jpg");
        Assert.Equal("Renamed.jpg", parsed["IMG001.jpg"]);
        var clone = new Rules { Mapping = parsed }.Clone();
        var item = new FileItem
        {
            Path = @"C:\photos\IMG001.jpg",
            Name = "IMG001.jpg",
            Stem = "IMG001",
            Ext = ".jpg",
            Folder = "photos",
            Parent = @"C:\photos"
        };
        Assert.Equal("Renamed.jpg", RenameEngine.ApplyRules(item, clone, 1).NewName);
    }

    [Fact]
    public void MappingExclusive_LeavesUnmappedFilesForOtherRules()
    {
        var mapped = new FileItem
        {
            Path = "/tmp/IMG001.jpg",
            Name = "IMG001.jpg",
            Stem = "IMG001",
            Ext = ".jpg",
            Folder = "tmp",
            Parent = "/tmp"
        };
        var other = new FileItem
        {
            Path = "/tmp/notes.txt",
            Name = "notes.txt",
            Stem = "notes",
            Ext = ".txt",
            Folder = "tmp",
            Parent = "/tmp"
        };
        var rules = new Rules
        {
            Mapping = new Dictionary<string, string> { ["IMG001.jpg"] = "Asset.jpg" },
            MappingExclusive = true,
            AddEnabled = true,
            Prefix = "KEEP_"
        };
        Assert.Equal("Asset.jpg", RenameEngine.ApplyRules(mapped, rules, 1).NewName);
        Assert.Equal("KEEP_notes.txt", RenameEngine.ApplyRules(other, rules, 1).NewName);
    }

    [Fact]
    public void ExifDate_SlashFormat_IsParsedForPhotoPreset()
    {
        var item = new FileItem
        {
            Path = "/tmp/DSC0001.jpg",
            Name = "DSC0001.jpg",
            Stem = "DSC0001",
            Ext = ".jpg",
            Folder = "tmp",
            Parent = "/tmp",
            Modified = new DateTime(2000, 1, 1),
            Exif = new Dictionary<string, string> { ["date"] = "14/09/2026 09:30:00" }
        };
        var (newName, _) = RenameEngine.ApplyRules(item, new Rules
        {
            DateEnabled = true,
            DateSource = "exif",
            DateFormat = "yyyy-MM-dd",
            DatePosition = "prefix"
        }, 1);
        Assert.Equal("2026-09-14_DSC0001.jpg", newName);
    }

    [Fact]
    public void FileSelection_EmptyChecks_ArePreservedOnRefresh()
    {
        FileItem Item(string name) => new()
        {
            Path = "/tmp/" + name,
            Name = name,
            Stem = Names.SplitName(name).Stem,
            Ext = Names.SplitName(name).Ext,
            Folder = "tmp",
            Parent = "/tmp"
        };

        var first = new[] { Item("a.txt"), Item("b.txt") };
        FileSelection.Apply(first, [], hadRows: false);
        Assert.All(first, i => Assert.True(i.Selected));

        var refreshed = new[] { Item("a.txt"), Item("b.txt") };
        FileSelection.Apply(refreshed, [], hadRows: true);
        Assert.All(refreshed, i => Assert.False(i.Selected));

        var partial = new[] { Item("a.txt"), Item("b.txt") };
        FileSelection.Apply(partial, ["/tmp/b.txt"], hadRows: true);
        Assert.False(partial[0].Selected);
        Assert.True(partial[1].Selected);
    }

    [Fact]
    public void PackagedBangersFont_InternalFamilyNameIsBangers()
    {
        var font = FindRepoFile(Path.Combine("src", "HulkReNaymer.App", "Assets", "Bangers-Regular.ttf"));
        Assert.True(File.Exists(font), font);
        var names = ReadTtfNameRecords(font);
        Assert.Contains("Bangers", names);
    }

    static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return relative;
    }

    static List<string> ReadTtfNameRecords(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        stream.Position = 4;
        var numTables = ReadUInt16(reader);
        stream.Position = 12;
        uint nameOffset = 0, nameLength = 0;
        for (var i = 0; i < numTables; i++)
        {
            var tag = new string(reader.ReadChars(4));
            reader.ReadUInt32();
            var offset = ReadUInt32(reader);
            var length = ReadUInt32(reader);
            if (tag == "name")
            {
                nameOffset = offset;
                nameLength = length;
            }
        }
        Assert.True(nameLength > 0);
        stream.Position = nameOffset;
        ReadUInt16(reader);
        var count = ReadUInt16(reader);
        var stringOffset = ReadUInt16(reader);
        var records = new List<(int Platform, int NameId, int Length, int Offset)>();
        for (var i = 0; i < count; i++)
        {
            var platform = ReadUInt16(reader);
            ReadUInt16(reader);
            ReadUInt16(reader);
            var nameId = ReadUInt16(reader);
            var length = ReadUInt16(reader);
            var offset = ReadUInt16(reader);
            records.Add((platform, nameId, length, offset));
        }
        var names = new List<string>();
        foreach (var rec in records.Where(r => r.NameId is 1 or 4 or 6))
        {
            stream.Position = nameOffset + stringOffset + rec.Offset;
            var bytes = reader.ReadBytes(rec.Length);
            var text = rec.Platform == 3
                ? System.Text.Encoding.BigEndianUnicode.GetString(bytes)
                : System.Text.Encoding.ASCII.GetString(bytes);
            names.Add(text);
        }
        return names;
    }

    static ushort ReadUInt16(BinaryReader reader)
    {
        var b = reader.ReadBytes(2);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return BitConverter.ToUInt16(b, 0);
    }

    static uint ReadUInt32(BinaryReader reader)
    {
        var b = reader.ReadBytes(4);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return BitConverter.ToUInt32(b, 0);
    }
}
