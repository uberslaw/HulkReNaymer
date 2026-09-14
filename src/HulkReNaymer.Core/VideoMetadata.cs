namespace HulkReNaymer;

public static class VideoMetadata
{
    static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".mkv", ".avi", ".wmv", ".webm", ".mpg", ".mpeg"
    };

    public static bool IsVideo(string path) =>
        VideoExts.Contains(Path.GetExtension(path));

    public static Dictionary<string, string> Read(string path)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!IsVideo(path) || !File.Exists(path)) return data;

        try
        {
            using var file = TagLib.File.Create(path);
            var duration = file.Properties?.Duration ?? TimeSpan.Zero;
            if (duration > TimeSpan.Zero)
                data["video.duration"] = FormatDuration(duration);
            if (file.Properties is { PhotoWidth: > 0 })
                data["video.width"] = file.Properties.PhotoWidth.ToString();
            if (file.Properties is { PhotoHeight: > 0 })
                data["video.height"] = file.Properties.PhotoHeight.ToString();
            if (file.Properties is { VideoWidth: > 0 })
                data["video.width"] = file.Properties.VideoWidth.ToString();
            if (file.Properties is { VideoHeight: > 0 })
                data["video.height"] = file.Properties.VideoHeight.ToString();
            if (file.Tag.Year > 0)
                data["video.date"] = file.Tag.Year.ToString("0000");
        }
        catch
        {
            /* not a readable media file */
        }

        // Same opt-in as photos: never spawn ExifTool just because it is on PATH.
        if (ExifToolReader.ExplicitlyConfigured)
        {
            foreach (var (key, value) in ExifToolReader.Read(path))
            {
                if (key.StartsWith("video.", StringComparison.OrdinalIgnoreCase) || key is "date")
                {
                    if (key == "date" && !data.ContainsKey("video.date"))
                        data["video.date"] = ExifToolReader.NormalizeDate(value);
                    else if (!data.ContainsKey(key))
                        data[key] = value;
                }
            }
        }

        return data;
    }

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours:00}-{duration.Minutes:00}-{duration.Seconds:00}";
        return $"{duration.Minutes:00}-{duration.Seconds:00}";
    }
}
