using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace HulkReNaymer;

public static class DemoSeeder
{
    public static string Seed(string? root = null)
    {
        var baseDir = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HulkReNaymer-Demo");
        Directory.CreateDirectory(baseDir);

        WriteTexts(Path.Combine(baseDir, "project"),
            "final report.docx", "cost plan.xlsx", "site photos.zip", "QLD Office Report.docx",
            "brisbane OFFICE asset REGISTER.xlsx", "AssetRegister.xlsx", "ProjectReport.pdf",
            "MeetingNotes.docx", "Report.pdf");
        WriteJpeg(Path.Combine(baseDir, "project", "Photo001.jpg"), 90, 200, 70);

        WriteTexts(Path.Combine(baseDir, "exports"),
            "EXPORT_20260914_Asset_Report_001.csv", "EXPORT_20260914_Asset_Report_002.csv");
        WriteJpeg(Path.Combine(baseDir, "exports", "IMG_2026_0001.jpg"), 50, 150, 50);

        var photos = Path.Combine(baseDir, "photos");
        Directory.CreateDirectory(photos);
        WriteJpeg(Path.Combine(photos, "DSC0001.jpg"), 46, 180, 68);
        WriteJpeg(Path.Combine(photos, "DSC0002.jpg"), 30, 140, 50);
        WriteJpeg(Path.Combine(photos, "DSC0003.jpg"), 80, 210, 90);
        WriteJpeg(Path.Combine(photos, "IMG001.jpg"), 20, 90, 40);
        WriteJpeg(Path.Combine(photos, "IMG002.jpg"), 40, 120, 60);
        WriteJpeg(Path.Combine(photos, "IMG003.jpg"), 60, 160, 80);
        WriteJpeg(Path.Combine(photos, "FILE01.JPEG"), 10, 70, 30);

        var music = Path.Combine(baseDir, "music");
        Directory.CreateDirectory(music);
        WriteMp3(Path.Combine(music, "Track01.mp3"));

        WriteTexts(Path.Combine(baseDir, "Brisbane"), "AssetList.xlsx");
        File.WriteAllText(Path.Combine(baseDir, "asset_register.txt"),
            "IMG001.jpg|AU123456_HP-ZBook.jpg\nIMG002.jpg|AU123457_HP-EliteBook.jpg\nIMG003.jpg|AU123458_HP-ZBook.jpg\n");
        return baseDir;
    }

    static void WriteTexts(string folder, params string[] names)
    {
        Directory.CreateDirectory(folder);
        foreach (var name in names)
            File.WriteAllText(Path.Combine(folder, name), "HulkReNaymer sample: " + name + "\n");
    }

    static void WriteJpeg(string path, byte r, byte g, byte b)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = new Image<Rgb24>(320, 240, new Rgb24(r, g, b));
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.DateTimeOriginal, "2026:09:14 09:30:00");
        exif.SetValue(ExifTag.DateTimeDigitized, "2026:09:14 09:30:00");
        exif.SetValue(ExifTag.Make, "HulkCam");
        exif.SetValue(ExifTag.Model, "Gamma");
        image.Metadata.ExifProfile = exif;
        image.SaveAsJpeg(path);
    }

    static void WriteMp3(string path)
    {
        var header = Convert.FromHexString("FFFB9064");
        var frame = header.Concat(new byte[104]).ToArray();
        using (var stream = File.Create(path))
        {
            for (var i = 0; i < 40; i++)
                stream.Write(frame);
        }
        try
        {
            using var file = TagLib.File.Create(path);
            file.Tag.Performers = ["Hulk Smash Band"];
            file.Tag.Album = "Gamma Rays";
            file.Tag.Title = "Street Inspection";
            file.Save();
        }
        catch
        {
            // Sample still exists even if tagging fails.
        }
    }
}
