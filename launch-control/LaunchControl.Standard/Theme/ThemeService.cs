using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace LaunchControl.Standard.Theme;

public sealed record ThemeRole(string Key, string Purpose, string DefaultHex, string Category);

public sealed record ThemeFontRole(string Key, string Purpose, string DefaultFamily, double DefaultSize);

public sealed record ThemeFont(string Family, double Size);

/// <summary>
/// Palette + fonts + optional glass. Instance-based so MLC can edit a child LC
/// theme without mutating MLC chrome. Current is the running app's session.
/// </summary>
public sealed class ThemeService
{
    public const int SlotCount = 10;
    public const double MinFontSize = 8;
    public const double MaxFontSize = 24;
    public const string DefaultFontFamily = "Segoe UI";

    public static IReadOnlyList<string> CategoryOrder { get; } =
        ["Background", "Card", "Button", "Text", "Status", "Border"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IReadOnlyList<ThemeRole> Roles { get; } =
    [
        new("ChromeColor", "Header chrome (top bar background)", "#2c344c", "Background"),
        new("PageBackgroundColor", "Page background", "#EEF0F5", "Background"),
        new("FooterBackgroundColor", "Footer bar background", "#F5F6FA", "Background"),
        new("CardBackgroundColor", "App card background", "#FFFFFF", "Card"),
        new("CardBorderColor", "App card border", "#C8CDDC", "Card"),
        new("PrimaryActionColor", "Primary action buttons (Open, Add app)", "#f97c14", "Button"),
        new("SecondaryButtonBackgroundColor", "Secondary button background", "#FFFFFF", "Button"),
        new("ButtonHoverColor", "Button hover", "#fcac66", "Button"),
        new("StrongWarmColor", "Attention button border / Unreachable status", "#b2541d", "Button"),
        new("OnChromeColor", "Header title text", "#FFFFFF", "Text"),
        new("HeaderMetaTextColor", "Header meta/status text", "#a4accc", "Text"),
        new("OnPrimaryActionColor", "Primary button text", "#2c344c", "Text"),
        new("AttentionButtonTextColor", "Attention button text", "#b2541d", "Text"),
        new("SecondaryButtonTextColor", "Secondary button text", "#2c344c", "Text"),
        new("CardTitleTextColor", "Card title text", "#2c344c", "Text"),
        new("PathTextColor", "Path text", "#956a58", "Text"),
        new("FooterTextColor", "Footer text", "#956a58", "Text"),
        new("BodyTextColor", "Body text (PID, health, version)", "#2c344c", "Text"),
        new("MetaTextColor", "Meta/secondary captions", "#a4accc", "Text"),
        new("MutedWarmColor", "Disabled borders / disabled button text", "#956a58", "Status"),
        new("ErrorColor", "Error text / Failed status", "#C62828", "Status"),
        new("StatusRunningColor", "Running status text", "#0E6B58", "Status"),
        new("SoftAccentColor", "Starting & Stopping status", "#c47a1a", "Status"),
        new("StatusStoppedColor", "Stopped status text", "#6B5348", "Status"),
        new("StatusUnknownColor", "Unknown status text", "#7A7A7A", "Status"),
        new("CoolMutedColor", "Footer & secondary button borders", "#a4accc", "Border"),
    ];

    private static readonly Dictionary<string, string[]> ColorToBrushes = new(StringComparer.Ordinal)
    {
        ["ChromeColor"] = ["ChromeBrush"],
        ["PrimaryActionColor"] = ["PrimaryActionBrush"],
        ["SecondaryButtonBackgroundColor"] = ["SecondaryButtonBackgroundBrush"],
        ["ButtonHoverColor"] = ["ButtonHoverBrush"],
        ["StrongWarmColor"] = ["StrongWarmBrush", "StatusUnreachableBrush"],
        ["MutedWarmColor"] = ["MutedWarmBrush", "DisabledBorderBrush"],
        ["CoolMutedColor"] = ["CoolMutedBrush"],
        ["PageBackgroundColor"] = ["PageBackgroundBrush"],
        ["CardBackgroundColor"] = ["CardBackgroundBrush"],
        ["CardBorderColor"] = ["CardBorderBrush"],
        ["FooterBackgroundColor"] = ["FooterBackgroundBrush"],
        ["ErrorColor"] = ["ErrorBrush", "StatusFailedBrush"],
        ["StatusRunningColor"] = ["StatusRunningBrush"],
        ["SoftAccentColor"] = ["SoftAccentBrush", "StatusStartingBrush", "StatusStoppingBrush"],
        ["StatusStoppedColor"] = ["StatusStoppedBrush"],
        ["StatusUnknownColor"] = ["StatusUnknownBrush"],
        ["OnChromeColor"] = ["OnChromeBrush"],
        ["HeaderMetaTextColor"] = ["HeaderMetaTextBrush"],
        ["OnPrimaryActionColor"] = ["OnPrimaryActionBrush"],
        ["AttentionButtonTextColor"] = ["AttentionButtonTextBrush"],
        ["SecondaryButtonTextColor"] = ["SecondaryButtonTextBrush"],
        ["CardTitleTextColor"] = ["CardTitleTextBrush"],
        ["PathTextColor"] = ["PathTextBrush"],
        ["FooterTextColor"] = ["FooterTextBrush"],
        ["BodyTextColor"] = ["BodyTextBrush"],
        ["MetaTextColor"] = ["MetaTextBrush"],
    };

    public static IReadOnlyList<ThemeFontRole> FontRoles { get; } =
    [
        new("Heading", "Card app name and window title", DefaultFontFamily, 18),
        new("Status", "Running / Service Running line", DefaultFontFamily, 12),
        new("Buttons", "Open LC, Start, Stop, and toolbar", DefaultFontFamily, 12),
        new("Details", "PID, health, version, path", DefaultFontFamily, 11),
        new("Body", "Everything else", DefaultFontFamily, 12),
    ];

    private static readonly string[] PreferredFontFamilies =
    [
        "Segoe UI", "Calibri", "Arial", "Consolas", "Cascadia Mono", "Cascadia Code",
        "Times New Roman", "Courier New", "Tahoma", "Verdana"
    ];

    private static IReadOnlyList<string>? _fontFamilyChoices;

    public static ThemeService? Current { get; private set; }

    public static event EventHandler? ThemeChanged;

    public string AppDataDir { get; }
    public string ThemePath { get; }
    public string SlotsPath { get; }
    public string? MirrorPath { get; set; }
    public bool ApplyToApplication { get; }
    public GlassState GlassState { get; } = new();
    public GlassSettings Glass { get; private set; } = new();

    private Dictionary<string, ThemeFont> _fonts;
    private Dictionary<string, string> _colors;
    private readonly IReadOnlyDictionary<string, string> _defaultColors;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _reloadDebounce;
    private bool _writing;
    private readonly object _gate = new();

    private ThemeService(
        string appDataDir,
        string themePath,
        string slotsPath,
        bool applyToApplication,
        IReadOnlyDictionary<string, string>? defaultColorOverrides)
    {
        AppDataDir = appDataDir;
        ThemePath = themePath;
        SlotsPath = slotsPath;
        ApplyToApplication = applyToApplication;
        _defaultColors = MergeDefaults(defaultColorOverrides);
        _fonts = CompleteFonts(null);
        _colors = CompletePalette(new Dictionary<string, string>(), _defaultColors);
    }

    /// <summary>Start the running app's theme (mutates Application.Resources, watches theme.json).</summary>
    public static ThemeService InitializeForApp(
        string appDataFolderName,
        IReadOnlyDictionary<string, string>? defaultColorOverrides = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            appDataFolderName);
        Directory.CreateDirectory(dir);
        var svc = new ThemeService(
            dir,
            Path.Combine(dir, "theme.json"),
            Path.Combine(dir, "theme-slots.json"),
            applyToApplication: true,
            defaultColorOverrides);
        Current = svc;
        svc.LoadAndApply(persist: false);
        svc.StartWatching();
        return svc;
    }

    /// <summary>Edit another product's theme.json without changing this process's chrome.</summary>
    public static ThemeService ForExternalFile(
        string themePath,
        string? slotsPath = null,
        string? mirrorPath = null,
        IReadOnlyDictionary<string, string>? defaultColorOverrides = null)
    {
        var dir = Path.GetDirectoryName(themePath)
                  ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaunchControl");
        Directory.CreateDirectory(dir);
        var svc = new ThemeService(
            dir,
            themePath,
            slotsPath ?? Path.Combine(dir, "theme-slots.json"),
            applyToApplication: false,
            defaultColorOverrides)
        {
            MirrorPath = mirrorPath
        };
        svc.LoadAndApply(persist: false);
        return svc;
    }

    public IReadOnlyDictionary<string, string> GetEffectiveHexMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var role in Roles)
            map[role.Key] = _colors.TryGetValue(role.Key, out var hex) ? hex : DefaultHex(role);
        return map;
    }

    public Dictionary<string, string> LoadOverrides() =>
        SanitizePalette(LoadDocument().Colors);

    public IReadOnlyDictionary<string, ThemeFont> GetEffectiveFontMap() =>
        CompleteFonts(_fonts);

    public void ApplyFonts(IReadOnlyDictionary<string, ThemeFont> fonts, bool persist)
    {
        void DoApply()
        {
            _fonts = CompleteFonts(fonts);
            if (ApplyToApplication)
                ApplyFontResources(Application.Current?.Resources, _fonts);
            if (persist)
                SaveDocument(_colors, _fonts, Glass);
            RaiseChanged();
        }

        Dispatch(DoApply);
    }

    public static IReadOnlyList<string> FontFamilyChoices =>
        _fontFamilyChoices ??= BuildFontFamilyChoices();

    public static IReadOnlyList<string> FontFamilyChoicesFor(string? extra)
    {
        IReadOnlyList<string> list;
        try
        {
            list = FontFamilyChoices;
        }
        catch (Exception ex)
        {
            Host.LcHostLog.Error("Enumerating font families failed; using built-in list", ex);
            list = PreferredFontFamilies;
        }

        if (string.IsNullOrWhiteSpace(extra)
            || list.Any(n => string.Equals(n, extra, StringComparison.OrdinalIgnoreCase)))
            return list;
        var withExtra = new List<string>(list.Count + 1) { extra.Trim() };
        withExtra.AddRange(list);
        return withExtra;
    }

    public static double ClampFontSize(double size) =>
        Math.Clamp(Math.Round(size), MinFontSize, MaxFontSize);

    public static string ResolveFontFamily(string? name) =>
        string.IsNullOrWhiteSpace(name) ? DefaultFontFamily : name.Trim();

    public void Apply(IReadOnlyDictionary<string, string> colors, bool persist) =>
        Apply(colors, Glass, persist);

    public void Apply(IReadOnlyDictionary<string, string> colors, GlassSettings glass, bool persist)
    {
        void DoApply()
        {
            var effective = CompletePalette(colors, _defaultColors);
            _colors = effective;
            Glass = glass.Clone();
            if (ApplyToApplication)
            {
                EnsureMutableBrushes();
                PushColorsToResources(effective);
                ApplyFontResources(Application.Current?.Resources, _fonts);
                PushGlassResources();
                ApplyGlassSurfaces();
            }

            if (persist)
                SaveDocument(effective, _fonts, Glass);

            RaiseChanged();
        }

        Dispatch(DoApply);
    }

    public void ApplyGlass(GlassSettings glass, bool persist)
    {
        Glass = glass.Clone();
        if (ApplyToApplication)
        {
            EnsureMutableBrushes();
            PushColorsToResources(_colors);
            PushGlassResources();
            ApplyGlassSurfaces();
        }
        if (persist)
            SaveDocument(_colors, _fonts, Glass);
        RaiseChanged();
    }

    public string PersistBackgroundImage(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("Background image not found.", sourcePath);

        var glassDir = Path.Combine(AppDataDir, "glass");
        Directory.CreateDirectory(glassDir);
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
        var dest = Path.Combine(glassDir, "background" + ext.ToLowerInvariant());
        File.Copy(sourcePath, dest, overwrite: true);
        if (!File.Exists(dest) || new FileInfo(dest).Length == 0)
            throw new IOException("Failed to copy background image into AppData.");
        return dest;
    }

    public Dictionary<string, string> SuggestGlassPalette(IReadOnlyDictionary<string, string> current)
    {
        var map = CompletePalette(current, _defaultColors);
        map["PageBackgroundColor"] = "#00000000";
        map["ChromeColor"] = WithAlpha(map["ChromeColor"], 0xA6);
        map["FooterBackgroundColor"] = WithAlpha("#1A1A1A", 0x90);
        map["CardBackgroundColor"] = "#40FFFFFF";
        map["SecondaryButtonBackgroundColor"] = "#30FFFFFF";
        map["PrimaryActionColor"] = WithAlpha(map["PrimaryActionColor"], 0x66);
        map["ButtonHoverColor"] = WithAlpha(map["ButtonHoverColor"], 0x88);
        map["CardBorderColor"] = "#99FFFFFF";
        map["CoolMutedColor"] = "#AAFFFFFF";

        foreach (var key in new[]
                 {
                     "OnChromeColor", "HeaderMetaTextColor", "CardTitleTextColor", "BodyTextColor",
                     "SecondaryButtonTextColor", "OnPrimaryActionColor", "FooterTextColor",
                     "PathTextColor", "MetaTextColor"
                 })
        {
            if (!map.TryGetValue(key, out var hex) || !TryParseHex(hex, out var c))
                continue;
            // Glass sits on a dark scrim; require light text against a dim reference.
            if (ContrastRatio(c, Color.FromRgb(0x3A, 0x3A, 0x3A)) < 4.5)
                map[key] = Invert(c);
        }

        map["OnChromeColor"] = "#FFFFFFFF";
        map["HeaderMetaTextColor"] = "#F0F4FF";
        map["OnPrimaryActionColor"] = "#FFFFFFFF";
        map["SecondaryButtonTextColor"] = "#FFFFFFFF";
        return map;
    }

    public void ResetToDefaults()
    {
        try
        {
            if (File.Exists(ThemePath))
                File.Delete(ThemePath);
        }
        catch { }

        _fonts = CompleteFonts(null);
        Glass = new GlassSettings();
        var defaults = CompletePalette(new Dictionary<string, string>(), _defaultColors);
        Apply(defaults, Glass, persist: false);
    }

    public IReadOnlyList<Dictionary<string, string>?> LoadSlots()
    {
        var slots = new Dictionary<string, string>?[SlotCount];
        try
        {
            if (!File.Exists(SlotsPath))
                return slots;
            var json = File.ReadAllText(SlotsPath);
            var file = JsonSerializer.Deserialize<ThemeSlotsFile>(json, JsonOptions);
            if (file?.Slots is null) return slots;
            for (var i = 0; i < SlotCount && i < file.Slots.Count; i++)
            {
                var raw = file.Slots[i];
                if (raw is null || raw.Count == 0) continue;
                var cleaned = SanitizePalette(raw);
                slots[i] = cleaned.Count > 0 ? cleaned : null;
            }
        }
        catch { }

        return slots;
    }

    public void SaveSlot(int index, IReadOnlyDictionary<string, string> colors)
    {
        if (index is < 0 or >= SlotCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        var slots = LoadSlots().ToArray();
        slots[index] = CompletePalette(colors, _defaultColors);
        WriteSlots(slots);
    }

    public void ClearSlot(int index)
    {
        if (index is < 0 or >= SlotCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        var slots = LoadSlots().ToArray();
        slots[index] = null;
        WriteSlots(slots);
    }

    public static bool TryParseHex(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith('#') || s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s.StartsWith('#') ? s[1..] : s[2..];
        if (s.Length is not (6 or 8)) return false;
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;
        if (s.Length == 6)
        {
            color = Color.FromRgb((byte)((value >> 16) & 0xFF), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF));
            return true;
        }

        color = Color.FromArgb(
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
        return true;
    }

    public static string NormalizeHex(string text)
    {
        if (!TryParseHex(text, out var c)) return text.Trim();
        return ToHex(c);
    }

    public static string ToHex(Color c) =>
        c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    public void StartWatching()
    {
        try
        {
            var dir = Path.GetDirectoryName(ThemePath);
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(ThemePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
            };
            _watcher.Changed += OnThemeFileChanged;
            _watcher.Created += OnThemeFileChanged;
            _watcher.EnableRaisingEvents = true;
        }
        catch { }
    }

    private void OnThemeFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_writing) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        dispatcher.BeginInvoke(() =>
        {
            _reloadDebounce?.Stop();
            _reloadDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _reloadDebounce.Tick += (_, _) =>
            {
                _reloadDebounce.Stop();
                if (_writing) return;
                LoadAndApply(persist: false);
            };
            _reloadDebounce.Start();
        });
    }

    private void LoadAndApply(bool persist)
    {
        var document = LoadDocument();
        _fonts = CompleteFonts(SanitizeFonts(document.Fonts));
        Glass = SanitizeGlass(document.Glass);
        Apply(SanitizePalette(document.Colors), Glass, persist);
    }

    private void PushGlassResources()
    {
        var app = Application.Current;
        if (app is null) return;
        var image = Glass.Enabled ? GlassState.TryLoadImage(Glass.BackgroundImagePath) : null;
        GlassState.CopyFrom(Glass, image);
        app.Resources["GlassState"] = GlassState;
        app.Resources["GlassOverlayOpacity"] = GlassState.OverlayOpacity;
        app.Resources["GlassEnabled"] = Glass.Enabled;
    }

    /// <summary>
    /// When glass is on, force translucent fills (console, buttons, chrome) so wallpaper
    /// shows through even if the saved palette is still opaque. Non-glass themes are left as stored.
    /// </summary>
    private void ApplyGlassSurfaces()
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        if (!Glass.Enabled)
        {
            RestoreConsoleBrushes(resources);
            return;
        }

        SetBrushColor(resources, "PageBackgroundBrush", Color.FromArgb(0, 0, 0, 0));
        ApplyFillAlpha(resources, "ChromeBrush", GlassAlpha(0x88, 0xC0));
        ApplyFillAlpha(resources, "FooterBackgroundBrush", GlassAlpha(0x70, 0xA8));
        ApplyFillAlpha(resources, "CardBackgroundBrush", GlassAlpha(0x28, 0x58));
        ApplyFillAlpha(resources, "SecondaryButtonBackgroundBrush", GlassAlpha(0x22, 0x52));
        ApplyFillAlpha(resources, "PrimaryActionBrush", GlassAlpha(0x40, 0x78));
        ApplyFillAlpha(resources, "ButtonHoverBrush", GlassAlpha(0x50, 0x90));

        var consoleTint = Color.FromArgb(GlassAlpha(0x3A, 0x80), 0x10, 0x12, 0x18);
        var consoleHeader = Color.FromArgb(GlassAlpha(0x48, 0x98), 0x18, 0x1C, 0x24);
        SetBrushColor(resources, "ConsoleBackgroundBrush", consoleTint);
        SetBrushColor(resources, "ConsoleHeaderBackgroundBrush", consoleHeader);
        SetBrushColor(resources, "ConsoleTextBrush", Color.FromRgb(0xEE, 0xF0, 0xF5));
        SetBrushColor(resources, "ConsoleMetaTextBrush", Color.FromRgb(0xC8, 0xD0, 0xDC));

        foreach (var brushKey in new[]
                 {
                     "OnChromeBrush", "HeaderMetaTextBrush", "BodyTextBrush", "MetaTextBrush",
                     "SecondaryButtonTextBrush", "OnPrimaryActionBrush", "CardTitleTextBrush",
                     "FooterTextBrush", "PathTextBrush"
                 })
        {
            EnsureLightBrush(resources, brushKey);
        }
    }

    /// <summary>Higher intensity = more photo = lower fill alpha.</summary>
    private byte GlassAlpha(byte morePhoto, byte moreFrost)
    {
        var t = 1 - Glass.Intensity;
        return (byte)Math.Clamp(morePhoto + (moreFrost - morePhoto) * t, 0, 255);
    }

    private static void ApplyFillAlpha(ResourceDictionary resources, string brushKey, byte alpha)
    {
        if (resources[brushKey] is not SolidColorBrush brush)
            return;
        var c = brush.Color;
        c.A = alpha;
        SetBrushColor(resources, brushKey, c);
    }

    private static void RestoreConsoleBrushes(ResourceDictionary resources)
    {
        SetBrushColor(resources, "ConsoleBackgroundBrush", Color.FromRgb(0x12, 0x12, 0x12));
        SetBrushColor(resources, "ConsoleHeaderBackgroundBrush", Color.FromRgb(0x20, 0x20, 0x20));
        SetBrushColor(resources, "ConsoleTextBrush", Color.FromRgb(0xDC, 0xDC, 0xDC));
        SetBrushColor(resources, "ConsoleMetaTextBrush", Color.FromRgb(0xAA, 0xAA, 0xAA));
    }

    private static void EnsureLightBrush(ResourceDictionary resources, string brushKey)
    {
        if (resources[brushKey] is not SolidColorBrush brush)
            return;
        if (RelativeLuminance(brush.Color) >= 0.55)
            return;
        SetBrushColor(resources, brushKey, Colors.White);
    }

    private void PushColorsToResources(Dictionary<string, string> effective)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;
        foreach (var role in Roles)
        {
            var hex = effective[role.Key];
            if (!TryParseHex(hex, out var color)) continue;
            resources[role.Key] = color;
            if (!ColorToBrushes.TryGetValue(role.Key, out var brushKeys)) continue;
            foreach (var brushKey in brushKeys)
                SetBrushColor(resources, brushKey, color);
        }
    }

    private void RaiseChanged() =>
        ThemeChanged?.Invoke(this, EventArgs.Empty);

    private void Dispatch(Action action)
    {
        var app = Application.Current;
        if (app is null || app.Dispatcher.CheckAccess())
            action();
        else
            app.Dispatcher.Invoke(action);
    }

    private void WriteSlots(Dictionary<string, string>?[] slots)
    {
        Directory.CreateDirectory(AppDataDir);
        var payload = new ThemeSlotsFile
        {
            Slots = slots.Select(s => s is null ? null : new Dictionary<string, string>(s, StringComparer.Ordinal)).ToList()
        };
        File.WriteAllText(SlotsPath, JsonSerializer.Serialize(payload, JsonOptions));
    }

    private void SaveDocument(Dictionary<string, string> colors, Dictionary<string, ThemeFont> fonts, GlassSettings glass)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ThemePath)!);
        var payload = BuildDocument(colors, fonts, glass);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        _writing = true;
        try
        {
            File.WriteAllText(ThemePath, json);
            if (!string.IsNullOrWhiteSpace(MirrorPath))
            {
                var mirrorDir = Path.GetDirectoryName(MirrorPath);
                if (!string.IsNullOrEmpty(mirrorDir))
                    Directory.CreateDirectory(mirrorDir);
                File.WriteAllText(MirrorPath, json);
            }
        }
        finally
        {
            _writing = false;
        }
    }

    private static ThemeDocument BuildDocument(
        Dictionary<string, string> colors, Dictionary<string, ThemeFont> fonts, GlassSettings glass)
    {
        var orderedColors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var role in Roles)
        {
            if (colors.TryGetValue(role.Key, out var hex))
                orderedColors[role.Key] = hex;
        }

        var orderedFonts = new Dictionary<string, FontSetting>(StringComparer.Ordinal);
        foreach (var role in FontRoles)
        {
            var font = fonts.TryGetValue(role.Key, out var value) ? value : new ThemeFont(role.DefaultFamily, role.DefaultSize);
            orderedFonts[role.Key] = new FontSetting { Family = font.Family, Size = font.Size };
        }

        return new ThemeDocument
        {
            Colors = orderedColors,
            Fonts = orderedFonts,
            Glass = new GlassDocument
            {
                Enabled = glass.Enabled,
                BackgroundImagePath = glass.BackgroundImagePath,
                Intensity = glass.Intensity,
                BlurRadius = glass.BlurRadius,
                Halo = glass.Halo
            }
        };
    }

    private ThemeDocument LoadDocument()
    {
        try
        {
            if (!File.Exists(ThemePath))
                return new ThemeDocument();
            var json = File.ReadAllText(ThemePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new ThemeDocument();
            var hasEnvelope = HasProperty(root, "colors") || HasProperty(root, "fonts") || HasProperty(root, "glass");
            if (hasEnvelope)
                return JsonSerializer.Deserialize<ThemeDocument>(json, JsonOptions) ?? new ThemeDocument();
            var flat = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            return new ThemeDocument { Colors = flat };
        }
        catch
        {
            return new ThemeDocument();
        }
    }

    private static bool HasProperty(JsonElement root, string name)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void ApplyFontResources(ResourceDictionary? resources, IReadOnlyDictionary<string, ThemeFont> fonts)
    {
        if (resources is null) return;
        foreach (var role in FontRoles)
        {
            var font = fonts.TryGetValue(role.Key, out var value)
                ? value
                : new ThemeFont(role.DefaultFamily, role.DefaultSize);
            resources[$"{role.Key}FontFamily"] = new FontFamily(ResolveFontFamily(font.Family));
            resources[$"{role.Key}FontSize"] = ClampFontSize(font.Size);
        }
    }

    private static IReadOnlyList<string> BuildFontFamilyChoices()
    {
        // Preferred list only. Enumerating Fonts.SystemFontFamilies on the UI thread
        // during Theme window construction froze Launch Control. Browse… still opens
        // the full Windows font dialog.
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var name in PreferredFontFamilies)
        {
            if (set.Add(name)) list.Add(name);
        }
        return list;
    }

    private static Dictionary<string, ThemeFont> SanitizeFonts(Dictionary<string, FontSetting>? raw)
    {
        var result = new Dictionary<string, ThemeFont>(StringComparer.Ordinal);
        if (raw is null) return result;
        foreach (var (key, setting) in raw)
        {
            if (string.IsNullOrWhiteSpace(key) || setting is null) continue;
            if (!FontRoles.Any(r => r.Key == key)) continue;
            var family = ResolveFontFamily(setting.Family);
            var size = setting.Size is > 0 and var s
                ? ClampFontSize(s)
                : FontRoles.First(r => r.Key == key).DefaultSize;
            result[key] = new ThemeFont(family, size);
        }
        return result;
    }

    private static Dictionary<string, ThemeFont> CompleteFonts(IReadOnlyDictionary<string, ThemeFont>? fonts)
    {
        var map = new Dictionary<string, ThemeFont>(StringComparer.Ordinal);
        foreach (var role in FontRoles)
        {
            map[role.Key] = fonts is not null && fonts.TryGetValue(role.Key, out var font)
                ? new ThemeFont(ResolveFontFamily(font.Family), ClampFontSize(font.Size))
                : new ThemeFont(role.DefaultFamily, role.DefaultSize);
        }
        return map;
    }

    private static Dictionary<string, string> SanitizePalette(Dictionary<string, string>? raw)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (raw is null) return result;
        foreach (var (key, value) in raw)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) continue;
            if (!Roles.Any(r => r.Key == key)) continue;
            if (!TryParseHex(value, out _)) continue;
            result[key] = NormalizeHex(value);
        }

        if (!result.ContainsKey("ButtonHoverColor")
            && raw.TryGetValue("SoftAccentColor", out var legacyHover)
            && TryParseHex(legacyHover, out _))
            result["ButtonHoverColor"] = NormalizeHex(legacyHover);

        if (!result.ContainsKey("SoftAccentColor"))
        {
            if (raw.TryGetValue("StatusStartingColor", out var starting) && TryParseHex(starting, out _))
                result["SoftAccentColor"] = NormalizeHex(starting);
            else if (raw.TryGetValue("StatusStoppingColor", out var stopping) && TryParseHex(stopping, out _))
                result["SoftAccentColor"] = NormalizeHex(stopping);
        }

        return result;
    }

    private static GlassSettings SanitizeGlass(GlassDocument? raw)
    {
        if (raw is null) return new GlassSettings();
        return new GlassSettings
        {
            Enabled = raw.Enabled,
            BackgroundImagePath = string.IsNullOrWhiteSpace(raw.BackgroundImagePath) ? null : raw.BackgroundImagePath,
            Intensity = Math.Clamp(raw.Intensity ?? 0.65, 0, 1),
            BlurRadius = Math.Clamp(raw.BlurRadius ?? 18, 0, 50),
            Halo = raw.Halo ?? true
        };
    }

    private Dictionary<string, string> CompletePalette(
        IReadOnlyDictionary<string, string> colors,
        IReadOnlyDictionary<string, string> defaults)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var role in Roles)
        {
            if (colors.TryGetValue(role.Key, out var hex) && TryParseHex(hex, out _))
                map[role.Key] = NormalizeHex(hex);
            else
                map[role.Key] = defaults.TryGetValue(role.Key, out var d) ? d : role.DefaultHex;
        }
        return map;
    }

    private string DefaultHex(ThemeRole role) =>
        _defaultColors.TryGetValue(role.Key, out var d) ? d : role.DefaultHex;

    private static Dictionary<string, string> MergeDefaults(IReadOnlyDictionary<string, string>? overrides)
    {
        var map = Roles.ToDictionary(r => r.Key, r => r.DefaultHex, StringComparer.Ordinal);
        if (overrides is null) return map;
        foreach (var (k, v) in overrides)
        {
            if (map.ContainsKey(k) && TryParseHex(v, out _))
                map[k] = NormalizeHex(v);
        }
        return map;
    }

    private static void EnsureMutableBrushes()
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;
        foreach (var brushKeys in ColorToBrushes.Values)
        {
            foreach (var key in brushKeys)
            {
                if (resources[key] is not SolidColorBrush brush) continue;
                if (!brush.IsFrozen) continue;
                resources[key] = brush.Clone();
            }
        }
    }

    private static void SetBrushColor(ResourceDictionary resources, string brushKey, Color color)
    {
        if (resources[brushKey] is SolidColorBrush existing)
        {
            if (existing.IsFrozen)
            {
                var clone = existing.Clone();
                clone.Color = color;
                resources[brushKey] = clone;
            }
            else
            {
                existing.Color = color;
            }
            return;
        }

        resources[brushKey] = new SolidColorBrush(color);
    }

    private static string WithAlpha(string hex, byte alpha)
    {
        if (!TryParseHex(hex, out var c)) return hex;
        c.A = alpha;
        return ToHex(c);
    }

    private static string Invert(Color c)
    {
        var inverted = Color.FromRgb((byte)(255 - c.R), (byte)(255 - c.G), (byte)(255 - c.B));
        return ToHex(inverted);
    }

    private static double ContrastRatio(Color a, Color b)
    {
        var l1 = RelativeLuminance(a);
        var l2 = RelativeLuminance(b);
        var light = Math.Max(l1, l2);
        var dark = Math.Min(l1, l2);
        return (light + 0.05) / (dark + 0.05);
    }

    private static double RelativeLuminance(Color c)
    {
        static double Lin(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    private sealed class ThemeSlotsFile
    {
        public List<Dictionary<string, string>?>? Slots { get; set; }
    }

    private sealed class ThemeDocument
    {
        public Dictionary<string, string>? Colors { get; set; }
        public Dictionary<string, FontSetting>? Fonts { get; set; }
        public GlassDocument? Glass { get; set; }
    }

    private sealed class GlassDocument
    {
        public bool Enabled { get; set; }
        public string? BackgroundImagePath { get; set; }
        public double? Intensity { get; set; }
        public double? BlurRadius { get; set; }
        public bool? Halo { get; set; }
    }

    private sealed class FontSetting
    {
        public string? Family { get; set; }
        public double? Size { get; set; }
    }
}
