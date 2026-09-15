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
    public void FavoriteJsonRoundTrip_KeepsCaseInsensitiveMapping()
    {
        var original = new Rules { Mapping = FavoritesStore.ParseMapping("img001.jpg|Renamed.jpg") };
        var json = System.Text.Json.JsonSerializer.Serialize(original);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<Rules>(json);
        Assert.NotNull(loaded);
        var item = new FileItem
        {
            Path = @"C:\photos\IMG001.jpg",
            Name = "IMG001.jpg",
            Stem = "IMG001",
            Ext = ".jpg",
            Folder = "photos",
            Parent = @"C:\photos"
        };
        Assert.Equal("Renamed.jpg", RenameEngine.ApplyRules(item, loaded, 1).NewName);
    }

    [Fact]
    public void WindowsPackagingInputs_MatchInstallerAndPublishAssumptions()
    {
        var root = FindRepoRoot();
        Assert.True(File.Exists(Path.Combine(root, "LICENSE")));
        Assert.True(File.Exists(Path.Combine(root, "src", "HulkReNaymer.App", "Assets", "hulk.ico")));
        Assert.True(File.Exists(Path.Combine(root, "src", "HulkReNaymer.App", "Assets", "hulk.png")));
        Assert.True(File.Exists(Path.Combine(root, "src", "HulkReNaymer.App", "Assets", "Bangers-Regular.ttf")));

        var csproj = File.ReadAllText(Path.Combine(root, "src", "HulkReNaymer.App", "HulkReNaymer.App.csproj"));
        Assert.Contains("<Version>1.0.0</Version>", csproj);
        Assert.Contains("<Resource Include=\"Assets\\Bangers-Regular.ttf\" />", csproj);
        Assert.Contains("<Resource Include=\"Assets\\hulk.png\" />", csproj);

        var profile = File.ReadAllText(Path.Combine(root, "src", "HulkReNaymer.App", "Properties", "PublishProfiles", "Win-x64.pubxml"));
        Assert.Contains("<RuntimeIdentifier>win-x64</RuntimeIdentifier>", profile);
        Assert.Contains("<SelfContained>true</SelfContained>", profile);
        Assert.Contains("<PublishTrimmed>false</PublishTrimmed>", profile);
        Assert.Contains("<DebugType>none</DebugType>", profile);
        Assert.Contains("<DebugSymbols>false</DebugSymbols>", profile);

        var iss = File.ReadAllText(Path.Combine(root, "setup", "HulkReNaymer.iss"));
        Assert.Contains("LicenseFile=..\\LICENSE", iss);
        Assert.Contains("SetupIconFile=..\\src\\HulkReNaymer.App\\Assets\\hulk.ico", iss);
        Assert.Contains("Source: \"..\\artifacts\\app\\*\"", iss);
        Assert.Contains("ArchitecturesAllowed=x64compatible", iss);
        Assert.Contains("DefaultDirName={autopf}\\{#MyAppName}", iss);

        var appXaml = File.ReadAllText(Path.Combine(root, "src", "HulkReNaymer.App", "App.xaml"));
        Assert.Contains("pack://application:,,,/Assets/#Bangers", appXaml);

        var script = File.ReadAllText(Path.Combine(root, "scripts", "build-windows.ps1"));
        Assert.Contains("-r win-x64", script);
        Assert.Contains("--self-contained true", script);
        Assert.Contains("DebugType=none", script);
        Assert.Contains("*.pdb", script);
        Assert.Contains("HulkReNaymer-Setup.exe", script);
        Assert.Contains("HulkReNaymer-portable-win-x64.zip", script);
    }

    [Fact]
    public void HashToken_IsSha256OfFileBytes_EmptyDirAndMissingAreBlank()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-hash-assumptions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var empty = Path.Combine(root, "empty.bin");
        var hello = Path.Combine(root, "hello.txt");
        var missing = Path.Combine(root, "gone.txt");
        File.WriteAllBytes(empty, []);
        File.WriteAllText(hello, "hello");
        try
        {
            var emptyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([])).ToLowerInvariant();
            Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", emptyHash);
            var emptyItem = MetadataReader.Describe(empty);
            Assert.Equal(emptyHash[..8], Tokens.Expand("{hash}", emptyItem, new Rules(), 1));
            Assert.Equal(emptyHash[..8], Tokens.Expand("{hash:8}", emptyItem, new Rules(), 1));
            Assert.Equal(emptyHash, Tokens.Expand("{hash:64}", emptyItem, new Rules(), 1));
            Assert.Equal(emptyHash[..1], Tokens.Expand("{hash:0}", emptyItem, new Rules(), 1));

            var helloItem = MetadataReader.Describe(hello);
            var helloHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes("hello"))).ToLowerInvariant();
            Assert.Equal(helloHash[..8], Tokens.Expand("{hash:8}", helloItem, new Rules(), 1));
            Assert.NotEqual(emptyHash[..8], helloHash[..8]);

            var folder = MetadataReader.Describe(root);
            Assert.True(folder.IsDir);
            Assert.Equal("", Tokens.Expand("{hash:8}", folder, new Rules(), 1));

            var gone = new FileItem
            {
                Path = missing,
                Name = "gone.txt",
                Stem = "gone",
                Ext = ".txt",
                Folder = Path.GetFileName(root),
                Parent = root
            };
            Assert.Equal("", Tokens.Expand("{hash:8}", gone, new Rules(), 1));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void GitBranch_EmptyWhenDetachedMissingGitOrOutsideRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-branch-assumptions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "notes.txt");
        File.WriteAllText(path, "x");
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            GitTestRepo.Init(root, "feat/foo");
            var item = MetadataReader.Describe(path);
            Assert.Equal("feat/foo", GitOps.CurrentBranch(path));
            Assert.Equal("feat_foo_notes.txt",
                RenameEngine.ApplyRules(item, new Rules { AddEnabled = true, Prefix = "{git.branch}_" }, 1).NewName);

            GitTestRepo.Run(root, "checkout", "--detach", "HEAD");
            Assert.Equal("", GitOps.CurrentBranch(path));
            Assert.Equal("", Tokens.Expand("{git.branch}", MetadataReader.Describe(path), new Rules(), 1));

            Environment.SetEnvironmentVariable("PATH", "");
            Assert.Equal("", GitOps.CurrentBranch(path));
            Assert.False(GitOps.TryMove(path, Path.Combine(root, "moved.txt")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void GitWorktree_DotGitFile_IsARepoRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-wt-main-" + Guid.NewGuid().ToString("N"));
        var work = Path.Combine(Path.GetTempPath(), "hulk-wt-work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "notes.txt"), "x");
        try
        {
            GitTestRepo.Init(root, "main");
            GitTestRepo.Run(root, "worktree", "add", "-b", "wt-lab", work);
            Assert.True(File.Exists(Path.Combine(work, ".git")));
            Assert.False(Directory.Exists(Path.Combine(work, ".git")));
            var tracked = Path.Combine(work, "notes.txt");
            Assert.Equal(Path.GetFullPath(work), Path.GetFullPath(GitOps.FindRepoRoot(tracked)!));
            Assert.Equal("wt-lab", GitOps.CurrentBranch(tracked));
        }
        finally
        {
            try { GitTestRepo.Run(root, "worktree", "remove", "--force", work); } catch { /* ignore */ }
            try { Directory.Delete(root, true); } catch { /* ignore */ }
            try { Directory.Delete(work, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void GitMv_FallsBackWhenNotARepo_OrGitMissing_AndUndoWorks()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-nogit-move-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("HULKRENAYMER_DATA", data);
        var path = Path.Combine(root, "plain.txt");
        File.WriteAllText(path, "payload");
        try
        {
            Assert.Null(GitOps.FindRepoRoot(path));
            Assert.False(GitOps.IsTracked(path));
            Assert.False(GitOps.TryMove(path, Path.Combine(root, "renamed.txt")));

            var item = MetadataReader.Describe(path);
            var rules = new Rules { AddEnabled = true, Prefix = "X_" };
            var result = ApplyService.Commit(RenameEngine.BuildPreview([item], rules), rules);
            Assert.Equal(1, result.Renamed);
            Assert.True(File.Exists(Path.Combine(root, "X_plain.txt")));
            Assert.Equal("payload", File.ReadAllText(Path.Combine(root, "X_plain.txt")));
            Assert.Equal(1, ApplyService.UndoLast().Renamed);
            Assert.True(File.Exists(path));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void GitMv_DifferentRepo_DoesNotUseGitMv()
    {
        var a = Path.Combine(Path.GetTempPath(), "hulk-repo-a-" + Guid.NewGuid().ToString("N"));
        var b = Path.Combine(Path.GetTempPath(), "hulk-repo-b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        File.WriteAllText(Path.Combine(a, "a.txt"), "A");
        File.WriteAllText(Path.Combine(b, "keep.txt"), "B");
        try
        {
            GitTestRepo.Init(a, "main");
            GitTestRepo.Init(b, "main");
            Assert.False(GitOps.TryMove(Path.Combine(a, "a.txt"), Path.Combine(b, "a.txt")));
            Assert.True(File.Exists(Path.Combine(a, "a.txt")));
        }
        finally
        {
            try { Directory.Delete(a, true); } catch { /* ignore */ }
            try { Directory.Delete(b, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void KebabKeepsPunctuation_SlugStripsUnicodePunctuation()
    {
        Assert.Equal("hello!!!-world.txt",
            RenameEngine.ApplyRules(Item("Hello!!!World.txt"), new Rules { CaseMode = "kebab" }, 1).NewName);
        Assert.Equal("hello-world.txt",
            RenameEngine.ApplyRules(Item("Hello!!!World.txt"), new Rules { CaseMode = "slug" }, 1).NewName);
        Assert.Equal("日本語-test.txt",
            RenameEngine.ApplyRules(Item("日本語 Test.txt"), new Rules { CaseMode = "kebab" }, 1).NewName);
        Assert.Equal("日本語-test.txt",
            RenameEngine.ApplyRules(Item("日本語 Test.txt"), new Rules { CaseMode = "slug" }, 1).NewName);
        Assert.Equal("cafe-au-lait.txt",
            RenameEngine.ApplyRules(Item("Café au lait!.txt"), new Rules { CaseMode = "slug" }, 1).NewName);
        Assert.Equal("café-au-lait!.txt",
            RenameEngine.ApplyRules(Item("Café au lait!.txt"), new Rules { CaseMode = "kebab" }, 1).NewName);
    }

    [Fact]
    public void TokenPicker_TargetsOnlyTokenBoxes_NotGridOrFilters()
    {
        var expected = new[]
        {
            "RegexReplace", "NameFixed", "ExtFixed", "ReplaceWithBox",
            "PrefixBox", "SuffixBox", "InsertBox", "DestDirBox"
        };
        Assert.Equal(expected.OrderBy(s => s), TokenCatalog.TargetBoxNames.OrderBy(s => s));
        foreach (var notATarget in new[] { "PathBox", "WildcardBox", "FindBox", "JsBox", "MappingBox", "DateFormatBox", "RegexPattern" })
            Assert.DoesNotContain(notATarget, TokenCatalog.TargetBoxNames);

        var xaml = File.ReadAllText(FindRepoFile(Path.Combine("src", "HulkReNaymer.App", "MainWindow.xaml")));
        Assert.Contains("CanUserAddRows=\"False\"", xaml);
        Assert.Contains("IsReadOnly=\"True\"", xaml);
        var code = File.ReadAllText(FindRepoFile(Path.Combine("src", "HulkReNaymer.App", "MainWindow.xaml.cs")));
        Assert.Contains("TokenCatalog.TargetBoxNames.Contains(box.Name)", code);
    }

    [Fact]
    public void ExifTool_IsOptInViaEnv_AndNormalizesNumericDuration()
    {
        ExifToolReader.ResetCache();
        var previous = Environment.GetEnvironmentVariable(ExifToolReader.PathEnv);
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        var root = Path.Combine(Path.GetTempPath(), "hulk-exiftool-assumptions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Environment.SetEnvironmentVariable(ExifToolReader.PathEnv, null);
            ExifToolReader.ResetCache();
            Assert.False(ExifToolReader.ExplicitlyConfigured);
            Assert.False(ExifToolReader.IsAvailable);

            if (!OperatingSystem.IsWindows())
            {
                var bin = Path.Combine(root, "bin");
                Directory.CreateDirectory(bin);
                var marker = Path.Combine(root, "ran.txt");
                var fake = Path.Combine(bin, "exiftool");
                File.WriteAllText(fake, "#!/bin/sh\necho ran > \"" + marker.Replace("\"", "") + "\"\nexit 0\n");
                File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + oldPath);
                ExifToolReader.ResetCache();
                var junk = Path.Combine(root, "clip.mp4");
                File.WriteAllText(junk, "not a video");
                var described = MetadataReader.Describe(junk);
                Assert.Equal("", Tokens.Expand("{video.duration}", described, new Rules(), 1));
                Assert.False(File.Exists(marker), "exiftool on PATH must not run unless HULKRENAYMER_EXIFTOOL is set");
            }

            Assert.Equal("01-05", ExifToolReader.NormalizeDuration("65.5"));
            Assert.Equal("01-02-03", ExifToolReader.NormalizeDuration("3723"));
            Assert.Equal("01-05", ExifToolReader.NormalizeDuration("0:01:05"));
            Assert.Equal("01-05", ExifToolReader.ParseJson("""[{"Duration": 65.5}]""")["video.duration"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ExifToolReader.PathEnv, previous);
            Environment.SetEnvironmentVariable("PATH", oldPath);
            ExifToolReader.ResetCache();
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TagLib_UnsupportedContainer_DoesNotAbortDescribe()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-taglib-junk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var name in new[] { "clip.mp4", "clip.mkv", "clip.webm", "clip.avi", "song.mp3" })
            File.WriteAllText(Path.Combine(root, name), "definitely not media");
        try
        {
            var (items, _) = Scanner.Scan(root);
            Assert.Equal(5, items.Count);
            Assert.All(items, item =>
            {
                Assert.False(item.Props.ContainsKey("video.duration"));
                Assert.Empty(item.Id3);
            });
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CollisionAppend_FirstKeepsName_SkipDropsAllClashers()
    {
        var rows = RenameEngine.BuildPreview(
            [Item("a.txt"), Item("b.txt")],
            new Rules { NameEnabled = true, NameMode = "fixed", NameFixed = "same", CollisionPolicy = "append" });
        Assert.Equal(new[] { "same.txt", "same_001.txt" }, rows.Select(r => r.NewName));
        Assert.Equal("", rows[0].Warning);

        var skipped = RenameEngine.BuildPreview(
            [Item("a.txt"), Item("b.txt")],
            new Rules { NameEnabled = true, NameMode = "fixed", NameFixed = "same", CollisionPolicy = "skip" });
        Assert.All(skipped, r => Assert.Equal("skipped", r.Status));
    }

    [Fact]
    public void CliApply_CommitsWithoutPrompt_WpfSmashAsks()
    {
        var cli = File.ReadAllText(FindRepoFile(Path.Combine("src", "HulkReNaymer.Cli", "CliApp.cs")));
        Assert.Contains("ApplyService.Commit(rows, rules)", cli);
        Assert.DoesNotContain("Console.Read", cli);
        var vm = File.ReadAllText(FindRepoFile(Path.Combine("src", "HulkReNaymer.App", "ViewModels", "MainViewModel.cs")));
        Assert.Contains("Commit rename?", vm);
        Assert.Contains("MessageBox.Show", vm);
    }

    [Fact]
    public void Cli_ScansEveryDirectoryArgument_AndSupportsDashedFilenames()
    {
        var root = Path.Combine(Path.GetTempPath(), "hulk-cli-dirs-" + Guid.NewGuid().ToString("N"));
        var a = Path.Combine(root, "photos");
        var b = Path.Combine(root, "docs");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        File.WriteAllText(Path.Combine(a, "one.txt"), "1");
        File.WriteAllText(Path.Combine(b, "two.txt"), "2");
        File.WriteAllText(Path.Combine(root, "-dashed.txt"), "d");
        try
        {
            var stdout = new System.IO.StringWriter();
            var code = HulkReNaymer.Cli.CliApp.Run(["preview", a, b, "--prefix", "X_"], stdout, new System.IO.StringWriter());
            Assert.Equal(0, code);
            var text = stdout.ToString();
            Assert.Contains("one.txt", text);
            Assert.Contains("two.txt", text);
            Assert.Contains("X_one.txt", text);
            Assert.Contains("X_two.txt", text);

            var dashed = new System.IO.StringWriter();
            Assert.Equal(0, HulkReNaymer.Cli.CliApp.Run(
                ["preview", "--prefix", "Z_", "--", Path.Combine(root, "-dashed.txt")],
                dashed, new System.IO.StringWriter()));
            Assert.Contains("Z_-dashed.txt", dashed.ToString());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ExplorerVerb_InstallerIsHkcrQuoted_ScriptIsHkcu()
    {
        var iss = File.ReadAllText(FindRepoFile(Path.Combine("setup", "HulkReNaymer.iss")));
        var script = File.ReadAllText(FindRepoFile(Path.Combine("scripts", "install-context-menu.ps1")));
        Assert.Contains("Root: HKCR;", iss);
        Assert.Contains("PrivilegesRequired=admin", iss);
        Assert.Contains("\"\"%1\"\"", iss);
        Assert.Contains("\"\"%V\"\"", iss);
        Assert.Contains("HKCU:\\Software\\Classes", script);
        Assert.DoesNotContain("HKLM:", script);
        Assert.Contains("Directory\\Background", script);
    }

    [Fact]
    public void RegexGroupsSurviveDateAliases_AndTokensExpandInDest()
    {
        var item = Item("Invoice 99.pdf");
        item = new FileItem
        {
            Path = item.Path,
            Name = item.Name,
            Stem = item.Stem,
            Ext = item.Ext,
            Folder = item.Folder,
            Parent = item.Parent,
            Modified = new DateTime(2026, 9, 14, 9, 0, 0)
        };
        Assert.Equal("Invoice_2026.pdf",
            RenameEngine.ApplyRules(item, new Rules
            {
                RegexEnabled = true,
                RegexPattern = @"^(Invoice) (\d+)$",
                RegexReplace = "$1_$YYYY"
            }, 1).NewName);
        var rows = RenameEngine.BuildPreview([item], new Rules { Operation = "copy", DestDir = "{yyyy}-{mm}" });
        Assert.EndsWith(Path.Combine("2026-09", "Invoice 99.pdf"), rows[0].NewPath);
    }

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
    public void MainViewModel_PreviewBrushesUseSolidColorBrush_NotColorCastToBrush()
    {
        var vm = File.ReadAllText(FindRepoFile(Path.Combine("src", "HulkReNaymer.App", "ViewModels", "MainViewModel.cs")));
        Assert.DoesNotContain("(Brush)ColorConverter.ConvertFromString", vm);
        Assert.Contains("new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!)", vm);
        Assert.Contains("static Brush HexBrush(string hex)", vm);
    }

    [Fact]
    public void WpfApp_HooksCrashLogBeforeStartupUriWindow()
    {
        var app = File.ReadAllText(FindRepoFile(Path.Combine("src", "HulkReNaymer.App", "App.xaml.cs")));
        Assert.Contains("DispatcherUnhandledException", app);
        Assert.Contains("AppDomain.CurrentDomain.UnhandledException", app);
        Assert.Contains("TaskScheduler.UnobservedTaskException", app);
        Assert.Contains("CrashLog.Write", app);
        Assert.Contains("CrashLog.AppLogPath", app);

        var log = File.ReadAllText(FindRepoFile(Path.Combine("src", "HulkReNaymer.Core", "CrashLog.cs")));
        Assert.Contains("app-crash.log", log);
        Assert.Contains("LocalApplicationData", log);
        Assert.Contains("HulkReNaymer", log);

        var window = File.ReadAllText(FindRepoFile(Path.Combine("src", "HulkReNaymer.App", "MainWindow.xaml.cs")));
        Assert.Contains("CrashLog.Write(\"MainWindow.ctor\"", window);
        Assert.Contains("InitializeComponent();", window);
        var init = window.IndexOf("InitializeComponent();", StringComparison.Ordinal);
        var vm = window.IndexOf("new MainViewModel()", StringComparison.Ordinal);
        Assert.True(init >= 0 && vm > init, "ViewModel must be constructed after InitializeComponent so a VM throw cannot skip XAML load.");
    }

    [Fact]
    public void CrashLog_WritesSourceAndExceptionWithoutThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), "hulk-crash-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            CrashLog.Write("unit-test", new InvalidCastException("Specified cast is not valid."), path);
            var text = File.ReadAllText(path);
            Assert.Contains("unit-test", text);
            Assert.Contains("InvalidCastException", text);
            Assert.Contains("Specified cast is not valid.", text);
            Assert.Contains("[FATAL]", text);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CrashLog_DefaultPathIsLocalAppDataLogs()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HulkReNaymer",
            "logs",
            "app-crash.log");
        Assert.Equal(expected, CrashLog.AppLogPath);
        Assert.Equal("app-crash.log", CrashLog.AppFileName);
    }

    [Fact]
    public void LaunchControl_AttachesHostLogBeforeFindingRoot()
    {
        var app = File.ReadAllText(FindRepoFile(Path.Combine("launch-control", "App.xaml.cs")));
        var attach = app.IndexOf("LcHostLog.AttachUnhandled", StringComparison.Ordinal);
        var find = app.IndexOf("LaunchActions.FindRoot", StringComparison.Ordinal);
        Assert.True(attach >= 0 && find >= 0 && attach < find);
        Assert.Contains("launch-control.log", app);
        Assert.Contains("LcHostLog.Fatal(ex, \"OnStartup\"", app);
    }

    [Fact]
    public void PackagedBangersFont_InternalFamilyNameIsBangers()
    {
        var font = FindRepoFile(Path.Combine("src", "HulkReNaymer.App", "Assets", "Bangers-Regular.ttf"));
        Assert.True(File.Exists(font), font);
        var names = ReadTtfNameRecords(font);
        Assert.Contains("Bangers", names);
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HulkReNaymer.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find HulkReNaymer.sln from " + AppContext.BaseDirectory);
    }

    static string FindRepoFile(string relative) => Path.Combine(FindRepoRoot(), relative);

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
