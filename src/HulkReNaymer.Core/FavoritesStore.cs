using System.Text.Json;

namespace HulkReNaymer;

public static class FavoritesStore
{
    static string PathName => System.IO.Path.Combine(ApplyService.DataDir, "favorites.json");

    public static List<Favorite> Load()
    {
        if (!File.Exists(PathName)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<Favorite>>(File.ReadAllText(PathName), JsonOptions) ?? [];
        }
        catch { return []; }
    }

    public static List<Favorite> Save(string name, Rules rules)
    {
        var favorites = Load().Where(f => f.Name != name).ToList();
        favorites.Add(new Favorite { Name = name, Rules = rules.Clone() });
        File.WriteAllText(PathName, JsonSerializer.Serialize(favorites, JsonOptions));
        return favorites;
    }

    public static Dictionary<string, string> ParseMapping(string text)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string oldName, newName;
            if (line.Contains('|'))
            {
                var parts = line.Split('|', 2);
                oldName = parts[0]; newName = parts[1];
            }
            else if (line.Contains(','))
            {
                var parts = line.Split(',', 2);
                oldName = parts[0]; newName = parts[1];
            }
            else if (line.Contains('\t'))
            {
                var parts = line.Split('\t', 2);
                oldName = parts[0]; newName = parts[1];
            }
            else continue;
            oldName = oldName.Trim();
            newName = newName.Trim();
            if (oldName.Length > 0 && newName.Length > 0)
                mapping[oldName] = newName;
        }
        return mapping;
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
