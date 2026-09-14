using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using SixLabors.ImageSharp;

namespace HulkReNaymer;

public static class MetadataReader
{
    public static FileItem Describe(string path, string? root = null)
    {
        var info = new FileInfo(path);
        var isDir = System.IO.Directory.Exists(path) && !info.Exists;
        DateTime? created = null, modified = null, accessed = null;
        long size = 0;
        try
        {
            if (isDir)
            {
                var dir = new DirectoryInfo(path);
                created = dir.CreationTime;
                modified = dir.LastWriteTime;
                accessed = dir.LastAccessTime;
            }
            else
            {
                created = info.CreationTime;
                modified = info.LastWriteTime;
                accessed = info.LastAccessTime;
                size = info.Length;
            }
        }
        catch { /* ignore */ }

        var name = Path.GetFileName(path);
        var (stem, ext) = isDir ? (name, "") : Names.SplitName(name);
        var parent = Path.GetDirectoryName(path) ?? "";
        var depth = 0;
        if (!string.IsNullOrEmpty(root))
        {
            try
            {
                var rel = Path.GetRelativePath(root, path);
                depth = Math.Max(rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length - 1, 0);
            }
            catch { /* ignore */ }
        }

        return new FileItem
        {
            Path = path,
            Name = name,
            Stem = stem,
            Ext = ext,
            Folder = Path.GetFileName(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            Parent = parent,
            IsDir = isDir,
            Size = size,
            Created = created,
            Modified = modified,
            Accessed = accessed,
            Depth = depth,
            Exif = isDir ? [] : ReadExif(path),
            Id3 = isDir ? [] : ReadId3(path),
            Props = isDir ? [] : ReadProps(path)
        };
    }

    public static Dictionary<string, string> ReadExif(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not ".jpg" and not ".jpeg" and not ".tif" and not ".tiff" and not ".webp")
            return [];
        var data = new Dictionary<string, string>();
        try
        {
            using var image = Image.Load(path);
            data["width"] = image.Width.ToString();
            data["height"] = image.Height.ToString();
        }
        catch { /* ignore */ }
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            foreach (var directory in directories)
            {
                if (directory is ExifSubIfdDirectory sub)
                {
                    var date = sub.GetDescription(ExifDirectoryBase.TagDateTimeOriginal)
                               ?? sub.GetDescription(ExifDirectoryBase.TagDateTimeDigitized);
                    if (!string.IsNullOrEmpty(date)) data["date"] = date;
                    var iso = sub.GetDescription(ExifDirectoryBase.TagIsoEquivalent);
                    var fnumber = sub.GetDescription(ExifDirectoryBase.TagFNumber);
                    var focal = sub.GetDescription(ExifDirectoryBase.TagFocalLength);
                    if (!string.IsNullOrEmpty(iso)) data["iso"] = iso;
                    if (!string.IsNullOrEmpty(fnumber)) data["fnumber"] = fnumber;
                    if (!string.IsNullOrEmpty(focal)) data["focal"] = focal;
                }
                if (directory is ExifIfd0Directory ifd0)
                {
                    var date = ifd0.GetDescription(ExifDirectoryBase.TagDateTime);
                    if (!data.ContainsKey("date") && !string.IsNullOrEmpty(date)) data["date"] = date;
                    var make = ifd0.GetDescription(ExifDirectoryBase.TagMake);
                    var model = ifd0.GetDescription(ExifDirectoryBase.TagModel);
                    if (!string.IsNullOrEmpty(make)) data["make"] = make;
                    if (!string.IsNullOrEmpty(model)) data["model"] = model;
                }
            }
        }
        catch { /* ignore */ }
        if (ExifToolReader.ExplicitlyConfigured)
        {
            foreach (var (key, value) in ExifToolReader.Read(path))
            {
                if (!string.IsNullOrWhiteSpace(value) && !data.ContainsKey(key))
                    data[key] = value;
            }
        }
        return data;
    }

    public static Dictionary<string, string> ReadId3(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not ".mp3" and not ".flac" and not ".ogg" and not ".m4a")
            return [];
        try
        {
            using var file = TagLib.File.Create(path);
            var data = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(file.Tag.FirstPerformer)) data["artist"] = file.Tag.FirstPerformer;
            if (!string.IsNullOrWhiteSpace(file.Tag.Album)) data["album"] = file.Tag.Album;
            if (!string.IsNullOrWhiteSpace(file.Tag.Title)) data["title"] = file.Tag.Title;
            if (!string.IsNullOrWhiteSpace(file.Tag.FirstGenre)) data["genre"] = file.Tag.FirstGenre;
            return data;
        }
        catch
        {
            return [];
        }
    }

    static Dictionary<string, string> ReadProps(string path)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".tif" or ".tiff")
        {
            try
            {
                using var image = Image.Load(path);
                data["width"] = image.Width.ToString();
                data["height"] = image.Height.ToString();
            }
            catch { /* ignore */ }
        }
        foreach (var (key, value) in VideoMetadata.Read(path))
            data[key] = value;
        return data;
    }
}
