using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HulkReNaymer;

namespace HulkReNaymer.App.ViewModels;

public sealed class FileRow : ObservableObject
{
    bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public string Path { get; init; } = "";
    public string OldName { get; init; } = "";
    public string NewName { get; init; } = "";
    public string Ext { get; init; } = "";
    public string SizeText { get; init; } = "";
    public string Modified { get; init; } = "";
    public string Folder { get; init; } = "";
    public string Status { get; init; } = "";
    public string Warning { get; init; } = "";
    public Brush NewNameBrush { get; init; } = Brushes.White;
    public event EventHandler? SelectionChanged;
}

public sealed class PlaceItem
{
    public required string Label { get; init; }
    public required string Path { get; init; }
}

public partial class MainViewModel : ObservableObject
{
    readonly Brush _ok = (Brush)ColorConverter.ConvertFromString("#7CFF4C")!;
    readonly Brush _danger = (Brush)ColorConverter.ConvertFromString("#FF5D6C")!;
    readonly Brush _skip = (Brush)ColorConverter.ConvertFromString("#6D8A70")!;
    readonly Brush _ink = (Brush)ColorConverter.ConvertFromString("#E7FFE0")!;

    [ObservableProperty] string currentPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    [ObservableProperty] string stats = "Ready.";
    [ObservableProperty] string mappingText = "";
    [ObservableProperty] string favoriteName = "";
    [ObservableProperty] string selectedPreset = "";
    [ObservableProperty] bool canRename;
    [ObservableProperty] bool recurse;
    [ObservableProperty] bool includeFiles = true;
    [ObservableProperty] bool includeFolders;
    [ObservableProperty] bool includeHidden;
    [ObservableProperty] string wildcard = "*";
    [ObservableProperty] string nameRegex = "";
    [ObservableProperty] string jsCode = "";

    List<string>? _explicitPaths;

    public ObservableCollection<FileRow> Files { get; } = [];
    public ObservableCollection<PlaceItem> Places { get; } = [];
    public ObservableCollection<PlaceItem> Folders { get; } = [];
    public ObservableCollection<string> Favorites { get; } = [];
    public string[] Presets { get; } = RulePresets.Names;

    public Rules Rules { get; private set; } = new();
    public event Action? RequestCollectRules;
    public event Action? RulesChanged;

    public MainViewModel()
    {
        RebuildPlaces();
        LoadFavoriteNames();
    }

    public void RebuildPlaces()
    {
        Places.Clear();
        AddPlace("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddPlace("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        AddPlace("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        AddPlace("Downloads", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        var demo = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HulkReNaymer-Demo");
        if (Directory.Exists(demo)) AddPlace("Demo files", demo);
    }

    void AddPlace(string label, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            Places.Add(new PlaceItem { Label = label, Path = path });
    }

    [RelayCommand]
    public void OpenPath(string? path = null)
    {
        var target = string.IsNullOrWhiteSpace(path) ? CurrentPath : path;
        if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
        {
            Stats = "Path not found.";
            return;
        }
        CurrentPath = System.IO.Path.GetFullPath(target);
        _explicitPaths = null;
        Files.Clear();
        Folders.Clear();
        var parent = Directory.GetParent(CurrentPath);
        if (parent is not null)
            Folders.Add(new PlaceItem { Label = "… parent", Path = parent.FullName });
        foreach (var (name, child) in Scanner.ListChildren(CurrentPath))
            Folders.Add(new PlaceItem { Label = name, Path = child });
        Refresh();
    }

    public void OpenFromCommandLine(IEnumerable<string> args)
    {
        var extras = args
            .Where(arg => File.Exists(arg) || Directory.Exists(arg))
            .ToList();
        if (extras.Count > 0)
            OpenItems(extras);
        else
            OpenPath(CurrentPath);
    }

    public void OpenItems(IEnumerable<string> paths)
    {
        var existing = paths
            .Select(p =>
            {
                try { return System.IO.Path.GetFullPath(p); }
                catch { return ""; }
            })
            .Where(p => p.Length > 0 && (File.Exists(p) || Directory.Exists(p)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (existing.Count == 0)
        {
            Stats = "No dropped or passed files were found.";
            return;
        }
        if (existing.Count == 1 && Directory.Exists(existing[0]) && !File.Exists(existing[0]))
        {
            OpenPath(existing[0]);
            return;
        }
        _explicitPaths = existing;
        CurrentPath = CommonParent(existing);
        Files.Clear();
        Folders.Clear();
        var parent = Directory.GetParent(CurrentPath);
        if (parent is not null)
            Folders.Add(new PlaceItem { Label = "… parent", Path = parent.FullName });
        foreach (var (name, child) in Scanner.ListChildren(CurrentPath))
            Folders.Add(new PlaceItem { Label = name, Path = child });
        Refresh();
        Stats = $"{existing.Count} dropped or passed items  ·  {Stats}";
    }

    (List<FileItem> Items, string Warning) ListItems()
    {
        if (_explicitPaths is { Count: > 0 })
            return Scanner.FromPaths(_explicitPaths, IncludeFiles, IncludeFolders || _explicitPaths.Any(Directory.Exists), IncludeHidden);
        return Scanner.Scan(CurrentPath, Recurse, IncludeFiles, IncludeFolders, Wildcard, NameRegex, includeHidden: IncludeHidden);
    }

    void ReloadCurrent()
    {
        if (_explicitPaths is { Count: > 0 })
            OpenItems(_explicitPaths);
        else
            OpenPath(CurrentPath);
    }

    static string CommonParent(IReadOnlyList<string> paths)
    {
        var first = System.IO.Path.GetDirectoryName(paths[0]);
        if (Directory.Exists(paths[0]) && !File.Exists(paths[0]))
            first = paths[0];
        if (string.IsNullOrEmpty(first)) return paths[0];
        var parent = first;
        foreach (var path in paths.Skip(1))
        {
            var dir = Directory.Exists(path) && !File.Exists(path) ? path : System.IO.Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) continue;
            while (!dir.StartsWith(parent.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(dir, parent, StringComparison.OrdinalIgnoreCase))
            {
                var up = Directory.GetParent(parent);
                if (up is null) return parent;
                parent = up.FullName;
            }
        }
        return parent;
    }

    [RelayCommand]
    public void GoUp()
    {
        var parent = Directory.GetParent(CurrentPath);
        if (parent is not null) OpenPath(parent.FullName);
    }

    [RelayCommand]
    public void Refresh()
    {
        RequestCollectRules?.Invoke();
        var (items, warning) = ListItems();
        var hadRows = Files.Count > 0;
        var selected = Files.Where(f => f.IsSelected).Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        FileSelection.Apply(items, selected, hadRows);
        var rows = RenameEngine.BuildPreview(items, Rules);
        Files.Clear();
        foreach (var row in rows)
        {
            var fileRow = new FileRow
            {
                IsSelected = row.Selected,
                Path = row.Path,
                OldName = row.OldName,
                NewName = row.NewName,
                Ext = row.Ext,
                SizeText = FormatSize(row.Size),
                Modified = row.Modified ?? "",
                Folder = row.Folder,
                Status = row.Status.ToUpperInvariant(),
                Warning = row.Warning,
                NewNameBrush = row.Status switch
                {
                    "ok" => _ok,
                    "conflict" or "invalid" or "exists" => _danger,
                    "unchanged" or "skipped" => _skip,
                    _ => _ink
                }
            };
            fileRow.SelectionChanged += (_, _) => Refresh();
            Files.Add(fileRow);
        }
        var changed = rows.Count(r => r.Changed && r.Status == "ok");
        var conflicts = rows.Count(r => r.Status == "conflict");
        var invalid = rows.Count(r => r.Status == "invalid");
        var exists = rows.Count(r => r.Status == "exists");
        var skipped = rows.Count(r => r.Status == "skipped");
        var blocking = Rules.CollisionPolicy is "skip" or "append" ? invalid : invalid + exists;
        CanRename = changed > 0 && conflicts == 0 && blocking == 0;
        var bits = new[]
        {
            $"{rows.Count} listed",
            $"{rows.Count(r => r.Selected)} selected",
            $"{changed} will change",
            $"{rows.Count(r => r.Status == "unchanged")} unchanged",
            $"{conflicts} conflicts",
            $"{exists} exist",
            $"{skipped} skipped",
            $"{invalid} invalid"
        };
        Stats = string.IsNullOrEmpty(warning) ? string.Join("  ·  ", bits) : string.Join("  ·  ", bits) + "  ·  " + warning;
    }

    [RelayCommand]
    public void Rename()
    {
        RequestCollectRules?.Invoke();
        var selected = Files.Where(f => f.IsSelected).Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var (items, _) = ListItems();
        foreach (var item in items)
            item.Selected = selected.Contains(item.Path);
        var rows = RenameEngine.BuildPreview(items, Rules);
        var changed = rows.Count(r => r.Changed && r.Status == "ok" && r.Selected);
        var confirm = MessageBox.Show(
            $"{changed} item(s) will be renamed.\n\nOnly selected items with a green preview are changed. Undo is available afterwards.",
            "Commit rename?",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;
        var result = ApplyService.Commit(rows, Rules);
        Rules = new Rules { WindowsSafe = true };
        MappingText = "";
        SelectedPreset = "";
        RulesChanged?.Invoke();
        ReloadCurrent();
        Stats = result.Message + "  ·  " + Stats;
        if (result.Failed.Count > 0)
            MessageBox.Show(string.Join("\n", result.Failed.Select(f => f.Path + ": " + f.Error)), "Some items failed");
    }

    [RelayCommand]
    public void Undo()
    {
        var result = ApplyService.UndoLast();
        ReloadCurrent();
        Stats = result.Message + "  ·  " + Stats;
    }

    [RelayCommand]
    public void SeedDemo()
    {
        var path = DemoSeeder.Seed();
        RebuildPlaces();
        OpenPath(path);
        Stats = "Demo files written to " + path;
    }

    [RelayCommand]
    public void ResetRules()
    {
        Rules = new Rules { WindowsSafe = true };
        MappingText = "";
        JsCode = "";
        SelectedPreset = "";
        OnPropertyChanged(nameof(Rules));
        RulesChanged?.Invoke();
        Refresh();
    }

    [RelayCommand]
    public void ImportMapping()
    {
        Rules.Mapping = FavoritesStore.ParseMapping(MappingText);
        Refresh();
        Stats = $"Imported {Rules.Mapping.Count} name mappings  ·  {Stats}";
    }

    [RelayCommand]
    public void SaveFavorite()
    {
        if (string.IsNullOrWhiteSpace(FavoriteName)) return;
        RequestCollectRules?.Invoke();
        FavoritesStore.Save(FavoriteName.Trim(), Rules);
        LoadFavoriteNames();
    }

    [RelayCommand]
    public void LoadFavorite(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var fav = FavoritesStore.Load().FirstOrDefault(f => f.Name == name);
        if (fav is null) return;
        Rules = fav.Rules.Clone();
        OnPropertyChanged(nameof(Rules));
        RulesChanged?.Invoke();
        Refresh();
    }

    [RelayCommand]
    public void ShowLog()
    {
        MessageBox.Show(string.IsNullOrWhiteSpace(ApplyService.ReadLog()) ? "(empty)" : ApplyService.ReadLog(), "Activity log");
    }

    public void ApplyPreset(string name)
    {
        Rules = RulePresets.Create(name);
        OnPropertyChanged(nameof(Rules));
        RulesChanged?.Invoke();
        Refresh();
    }

    void LoadFavoriteNames()
    {
        Favorites.Clear();
        foreach (var fav in FavoritesStore.Load())
            Favorites.Add(fav.Name);
    }

    static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "";
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }
}
