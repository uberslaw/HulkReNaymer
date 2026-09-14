using HulkReNaymer;

namespace HulkReNaymer.Tests;

public class Phase4Tests
{
    [Fact]
    public void VideoDuration_FormatsForFilenames()
    {
        Assert.Equal("01-05", VideoMetadata.FormatDuration(TimeSpan.FromSeconds(65)));
        Assert.Equal("01-02-03", VideoMetadata.FormatDuration(new TimeSpan(1, 2, 3)));
        Assert.Equal("00-00", VideoMetadata.FormatDuration(TimeSpan.Zero));
    }

    [Fact]
    public void VideoTokens_ExpandFromProps_AndSkipJunkFiles()
    {
        var item = new FileItem
        {
            Path = "/tmp/clip.mp4",
            Name = "clip.mp4",
            Stem = "clip",
            Ext = ".mp4",
            Folder = "tmp",
            Parent = "/tmp",
            Props = new Dictionary<string, string>
            {
                ["video.duration"] = "01-05",
                ["video.date"] = "2026-09-14"
            }
        };
        Assert.Equal("2026-09-14_01-05.mp4",
            RenameEngine.ApplyRules(item, new Rules { NameEnabled = true, NameMode = "fixed", NameFixed = "{video.date}_{video.duration}" }, 1).NewName);

        var junk = Path.Combine(Path.GetTempPath(), "hulk-vid-" + Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllText(junk, "not a video");
        try
        {
            var described = MetadataReader.Describe(junk);
            Assert.False(described.Props.ContainsKey("video.duration"));
            Assert.Equal("", Tokens.Expand("{video.duration}", described, new Rules(), 1));
        }
        finally
        {
            try { File.Delete(junk); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ExifTool_ParseJson_MapsCameraAndVideo_AndIsOptional()
    {
        ExifToolReader.ResetCache();
        Environment.SetEnvironmentVariable(ExifToolReader.PathEnv, "");
        Assert.False(ExifToolReader.ExplicitlyConfigured);

        var json = """
            [{
              "Make": "HulkCam",
              "Model": "Gamma",
              "DateTimeOriginal": "2026:09:14 09:30:00",
              "CreateDate": "2026:09:14 09:31:00",
              "Duration": 65.5,
              "ImageWidth": 1920,
              "ImageHeight": 1080
            }]
            """;
        var data = ExifToolReader.ParseJson(json);
        Assert.Equal("HulkCam", data["make"]);
        Assert.Equal("Gamma", data["model"]);
        Assert.Equal("2026-09-14 09:30:00", data["date"]);
        Assert.Equal("2026-09-14 09:31:00", data["video.date"]);
        Assert.Equal("65.5", data["video.duration"]);
        Assert.Equal("1920", data["width"]);

        var missing = Path.Combine(Path.GetTempPath(), "no-such-exiftool-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(ExifToolReader.PathEnv, missing);
        ExifToolReader.ResetCache();
        Assert.True(ExifToolReader.ExplicitlyConfigured);
        Assert.Empty(ExifToolReader.Read("/tmp/does-not-exist.jpg"));
        Environment.SetEnvironmentVariable(ExifToolReader.PathEnv, null);
        ExifToolReader.ResetCache();
    }

    [Fact]
    public void TokenCatalog_IncludesVideoTokens()
    {
        Assert.Contains(TokenCatalog.All, t => t.Token == "{video.date}");
        Assert.Contains(TokenCatalog.All, t => t.Token == "{video.duration}");
    }

    [Fact]
    public void InstallerAndScripts_DeclareOptionalExplorerVerb()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var iss = File.ReadAllText(Path.Combine(root, "setup", "HulkReNaymer.iss"));
        var script = File.ReadAllText(Path.Combine(root, "scripts", "install-context-menu.ps1"));
        Assert.Contains("Name: \"contextmenu\"", iss);
        Assert.Contains("Flags: unchecked", iss);
        Assert.Contains("*\\shell\\HulkReNaymer", iss);
        Assert.Contains("Directory\\shell\\HulkReNaymer", iss);
        Assert.DoesNotContain("HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run", iss);
        Assert.Contains("HKCU:\\Software\\Classes", script);
        Assert.Contains("-Remove", script);
        Assert.DoesNotContain("CurrentVersion\\Run", script);
    }
}
