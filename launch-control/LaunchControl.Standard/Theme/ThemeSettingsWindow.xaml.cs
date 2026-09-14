using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DrawingColor = System.Drawing.Color;
using WinForms = System.Windows.Forms;
using Microsoft.Win32;
using LaunchControl.Standard.Host;

namespace LaunchControl.Standard.Theme;

public partial class ThemeSettingsWindow : Window
{
    private const int MaxUndoDepth = 20;

    /// <summary>Theme-window-local category row tints (not app DynamicResource).</summary>
    private static readonly Dictionary<string, string> CategoryTintHex = new(StringComparer.Ordinal)
    {
        ["Background"] = "#E4E8F0",
        ["Card"] = "#EEF0F6",
        ["Button"] = "#F5EBE3",
        ["Text"] = "#E6EEF2",
        ["Status"] = "#F2E6E6",
        ["Border"] = "#E8EAEF",
    };

    private readonly ThemeService _theme;
    private readonly IThemeHighlightHost? _highlightHost;
    private readonly ObservableCollection<ThemeColorRow> _rows = new();
    private readonly ObservableCollection<ThemeCategoryGroup> _categoryGroups = new();
    private readonly ObservableCollection<ThemeSlotRow> _slots = new();
    private readonly ObservableCollection<ThemeFontRoleRow> _fontRows = new();
    private readonly List<Dictionary<string, string>> _undoStack = new();
    private Dictionary<string, string> _lastApplied;
    private bool _suppressLive;
    private int _selectedSlotIndex;
    private string? _pinnedInspectKey;
    private string? _hoverInspectKey;
    private bool _suppressGlassUi;
    private bool _chromeReady;

    public ThemeSettingsWindow()
        : this(ThemeService.Current ?? throw new InvalidOperationException("ThemeService.InitializeForApp was not called."), null, null)
    {
    }

    public ThemeSettingsWindow(ThemeService theme, IThemeHighlightHost? highlightHost = null, string? targetName = null)
    {
        LcHostLog.Info("ThemeSettingsWindow ctor start");
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _highlightHost = highlightHost;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            try { PositionBesideOwner(); }
            catch (Exception ex) { LcHostLog.Error("PositionBesideOwner failed", ex); }
        };

        TargetLabel.Text = string.IsNullOrWhiteSpace(targetName)
            ? (_theme.ApplyToApplication
                ? "Editing this window's theme (Colors, Font, Going glass). Apply saves this Launch Control only."
                : $"Editing {System.IO.Path.GetFileName(_theme.ThemePath)} (Apply writes the product theme including Going glass; this app's chrome is unchanged).")
            : $"Editing theme for {targetName} (colors, fonts, and Going glass). Apply writes that product's theme.json — this window's chrome stays as it is.";

        var map = _theme.GetEffectiveHexMap();
        foreach (var role in ThemeService.Roles)
        {
            var hex = map.TryGetValue(role.Key, out var h) ? h : role.DefaultHex;
            var tintHex = CategoryTintHex.TryGetValue(role.Category, out var t) ? t : "#FFFFFF";
            var row = new ThemeColorRow(role.Key, role.Purpose, role.Category, tintHex, hex);
            row.HexChanged += OnRowHexChanged;
            _rows.Add(row);
        }

        CategoryList.ItemsSource = _categoryGroups;
        RebuildCategoryGroups();
        _lastApplied = CloneMap(map);

        var fonts = _theme.GetEffectiveFontMap();
        foreach (var role in ThemeService.FontRoles)
        {
            var font = fonts.TryGetValue(role.Key, out var value)
                ? value
                : new ThemeFont(role.DefaultFamily, role.DefaultSize);
            var row = new ThemeFontRoleRow(role, font.Family, font.Size, ThemeService.FontFamilyChoicesFor(font.Family));
            row.FontChanged += OnFontRoleChanged;
            _fontRows.Add(row);
        }

        FontRoleList.ItemsSource = _fontRows;

        for (var i = 0; i < ThemeService.SlotCount; i++)
            _slots.Add(new ThemeSlotRow(i));
        SlotList.ItemsSource = _slots;
        ReloadSlotsUi();
        SelectSlot(0);
        UpdateUndoUi();
        LoadGlassUiFromTheme();
        _chromeReady = true;
        LcHostLog.Info($"ThemeSettingsWindow ctor done. ThemePath={_theme.ThemePath} GlassEnabled={_theme.Glass.Enabled}");
    }

    /// <summary>
    /// Place to the left of the owner (main) window; clamp into the monitor work area.
    /// </summary>
    private void PositionBesideOwner()
    {
        if (Owner is not Window owner)
            return;

        const double gap = 12;
        var width = double.IsNaN(Width) || Width <= 0 ? 1080 : Width;
        var height = double.IsNaN(Height) || Height <= 0 ? 720 : Height;
        var left = owner.Left - width - gap;
        var top = owner.Top;

        var work = SystemParameters.WorkArea;
        if (left < work.Left)
            left = work.Left;
        if (top < work.Top)
            top = work.Top;
        if (left + width > work.Right)
            left = Math.Max(work.Left, work.Right - width);
        if (top + height > work.Bottom)
            top = Math.Max(work.Top, work.Bottom - height);

        Left = left;
        Top = top;
    }

    /// <summary>
    /// Switch to Colors, highlight matching role rows, scroll first match into view.
    /// Clears the filter so highlighted rows are always visible.
    /// </summary>
    public void HighlightRoles(IReadOnlyList<string> keys)
    {
        ThemeTabs.SelectedIndex = 0;

        if (!string.IsNullOrEmpty(ColorFilterBox.Text))
            ColorFilterBox.Text = "";

        var set = new HashSet<string>(keys, StringComparer.Ordinal);
        ThemeColorRow? first = null;
        foreach (var row in _rows)
        {
            var match = set.Contains(row.Key);
            row.IsHighlighted = match;
            if (match && first is null)
                first = row;
        }

        if (keys.Count == 0)
        {
            SelectedRolesLabel.Visibility = Visibility.Collapsed;
            SelectedRolesLabel.Text = "";
        }
        else
        {
            SelectedRolesLabel.Text = "Selected: " + string.Join(", ", keys);
            SelectedRolesLabel.Visibility = Visibility.Visible;
        }

        Activate();

        if (first is null)
            return;

        var target = first;
        Dispatcher.BeginInvoke(() =>
        {
            var container = FindRowContainer(target);
            container?.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private void ColorFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        ColorFilterPlaceholder.Visibility = string.IsNullOrEmpty(ColorFilterBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        RebuildCategoryGroups();
    }

    private void RebuildCategoryGroups()
    {
        var filter = ColorFilterBox.Text?.Trim() ?? "";
        _categoryGroups.Clear();

        foreach (var category in ThemeService.CategoryOrder)
        {
            var matching = _rows
                .Where(r => string.Equals(r.Category, category, StringComparison.Ordinal)
                            && MatchesFilter(r, filter))
                .ToList();
            if (matching.Count == 0)
                continue;

            var group = new ThemeCategoryGroup(category);
            foreach (var row in matching)
                group.Rows.Add(row);
            _categoryGroups.Add(group);
        }
    }

    private static bool MatchesFilter(ThemeColorRow row, string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return true;

        return row.Purpose.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || row.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || row.Category.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private FrameworkElement? FindRowContainer(ThemeColorRow row) =>
        FindContainerWithDataContext(CategoryList, row);

    private static FrameworkElement? FindContainerWithDataContext(DependencyObject root, object data)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && ReferenceEquals(fe.DataContext, data))
                return fe;
            var nested = FindContainerWithDataContext(child, data);
            if (nested is not null)
                return nested;
        }
        return null;
    }

    private void OnRowHexChanged(ThemeColorRow row)
    {
        if (_suppressLive || LivePreviewCheck.IsChecked != true)
            return;
        if (!ThemeService.TryParseHex(row.HexText, out _))
            return;
        ApplyCurrent(persist: false);
    }

    private void OnFontRoleChanged(ThemeFontRoleRow _)
    {
        if (_suppressLive || LivePreviewCheck.IsChecked != true)
            return;
        ApplyFontsFromRows(persist: false);
    }

    private void ApplyFontsFromRows(bool persist)
    {
        var map = new Dictionary<string, ThemeFont>(StringComparer.Ordinal);
        foreach (var row in _fontRows)
            map[row.Key] = new ThemeFont(ThemeService.ResolveFontFamily(row.Family), ThemeService.ClampFontSize(row.Size));
        _theme.ApplyFonts(map, persist);
    }

    private void LoadFontRowsFrom(IReadOnlyDictionary<string, ThemeFont> fonts)
    {
        _suppressLive = true;
        try
        {
            foreach (var row in _fontRows)
            {
                var role = ThemeService.FontRoles.First(r => r.Key == row.Key);
                var font = fonts.TryGetValue(row.Key, out var value)
                    ? value
                    : new ThemeFont(role.DefaultFamily, role.DefaultSize);
                row.Set(font.Family, font.Size);
            }
        }
        finally
        {
            _suppressLive = false;
        }
    }

    private void FontSizeMinus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ThemeFontRoleRow row })
            return;
        row.Size = ThemeService.ClampFontSize(row.Size - 1);
    }

    private void FontSizePlus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ThemeFontRoleRow row })
            return;
        row.Size = ThemeService.ClampFontSize(row.Size + 1);
    }

    private void ResetFontRole_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ThemeFontRoleRow row })
            return;
        var role = ThemeService.FontRoles.First(r => r.Key == row.Key);
        row.Set(role.DefaultFamily, role.DefaultSize);
        if (LivePreviewCheck.IsChecked == true)
            ApplyFontsFromRows(persist: false);
    }

    private void ResetAllFonts_Click(object sender, RoutedEventArgs e)
    {
        _suppressLive = true;
        try
        {
            foreach (var row in _fontRows)
            {
                var role = ThemeService.FontRoles.First(r => r.Key == row.Key);
                row.Set(role.DefaultFamily, role.DefaultSize);
            }
        }
        finally
        {
            _suppressLive = false;
        }

        if (LivePreviewCheck.IsChecked == true)
            ApplyFontsFromRows(persist: false);
    }

    private void BrowseFont_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ThemeFontRoleRow row })
            return;

        LcHostLog.Info($"Theme Browse font for {row.Key}");
        try
        {
            using var dlg = new WinForms.FontDialog
            {
                FontMustExist = true,
                ShowEffects = false,
                AllowScriptChange = false,
                AllowVerticalFonts = false,
            };

            try
            {
                dlg.Font = new System.Drawing.Font(row.Family, (float)Math.Max(8, row.Size));
            }
            catch
            {
                dlg.Font = new System.Drawing.Font(ThemeService.DefaultFontFamily, 12f);
            }

            var hwnd = new WindowInteropHelper(this).Handle;
            var owner = new Win32Window(hwnd);
            if (dlg.ShowDialog(owner) != WinForms.DialogResult.OK)
                return;

            row.Set(dlg.Font.Name, ThemeService.ClampFontSize(dlg.Font.Size));
            if (LivePreviewCheck?.IsChecked == true)
                ApplyFontsFromRows(persist: false);
        }
        catch (Exception ex)
        {
            LcHostLog.Error("Font dialog failed", ex);
            MessageBox.Show(this, ex.Message, "Theme settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PickColor_Click(object sender, RoutedEventArgs e) =>
        OpenColorPicker(sender);

    private void PickColor_Click(object sender, MouseButtonEventArgs e)
    {
        OpenColorPicker(sender);
        e.Handled = true;
    }

    private void OpenColorPicker(object sender)
    {
        if (sender is not FrameworkElement { DataContext: ThemeColorRow row })
            return;

        using var dlg = new WinForms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            SolidColorOnly = true,
        };

        if (ThemeService.TryParseHex(row.HexText, out var current))
            dlg.Color = DrawingColor.FromArgb(current.R, current.G, current.B);

        var owner = new Win32Window(new WindowInteropHelper(this).Handle);
        if (dlg.ShowDialog(owner) != WinForms.DialogResult.OK)
            return;

        row.HexText = ThemeService.ToHex(Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
    }

    private sealed class Win32Window(IntPtr handle) : WinForms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        LcHostLog.Info("Theme Apply clicked");
        if (!TryBuildMap(out var map, out var error))
        {
            MessageBox.Show(this, error, "Theme settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            ApplyMap(map, persist: true);
            ApplyFontsFromRows(persist: true);
            LcHostLog.Info($"Theme Apply saved {_theme.ThemePath} glassEnabled={_theme.Glass.Enabled} image={_theme.Glass.BackgroundImagePath ?? "(none)"}");
        }
        catch (Exception ex)
        {
            LcHostLog.Error("Theme Apply failed", ex);
            MessageBox.Show(this,
                "Apply failed. Details were written to:\n" + LcHostLog.LogPath + "\n\n" + ex.Message,
                "Theme settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count == 0)
            return;

        var previous = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        _suppressLive = true;
        try
        {
            foreach (var row in _rows)
            {
                var hex = previous.TryGetValue(row.Key, out var h)
                    ? h
                    : ThemeService.Roles.First(r => r.Key == row.Key).DefaultHex;
                row.SetHex(hex);
            }
        }
        finally
        {
            _suppressLive = false;
        }

        _theme.Apply(previous, persist: true);
        _lastApplied = CloneMap(previous);
        RefreshSwatchesFromApplied();
        UpdateUndoUi();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(this,
            "Reset all colors and fonts to the built-in defaults and clear saved theme overrides?",
            "Reset theme",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes)
            return;

        PushUndoSnapshot(_lastApplied);

        _theme.ResetToDefaults();
        var defaults = ThemeService.Roles.ToDictionary(r => r.Key, r => r.DefaultHex, StringComparer.Ordinal);

        _suppressLive = true;
        try
        {
            foreach (var row in _rows)
                row.SetHex(defaults[row.Key]);
        }
        finally
        {
            _suppressLive = false;
        }

        _lastApplied = CloneMap(defaults);
        LoadFontRowsFrom(_theme.GetEffectiveFontMap());
        LoadGlassUiFromTheme();
        RefreshSwatchesFromApplied();
        UpdateUndoUi();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        LcHostLog.Info("Theme Close clicked");
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        LcHostLog.Info("ThemeSettingsWindow OnClosed");
        _pinnedInspectKey = null;
        _hoverInspectKey = null;
        _highlightHost?.ClearThemeRoleHighlight();
        base.OnClosed(e);
    }

    private void ColorRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ThemeColorRow row })
            return;
        _hoverInspectKey = row.Key;
        // While a row is pinned, hover must not change the main-window highlight.
        if (_pinnedInspectKey is not null)
            return;
        SyncMainRoleHighlight();
    }

    private void ColorRow_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoverInspectKey = null;
        if (_pinnedInspectKey is not null)
            return;
        SyncMainRoleHighlight();
    }

    private void ColorRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ShouldIgnoreInspectClick(e.OriginalSource as DependencyObject))
            return;
        if (sender is not FrameworkElement { DataContext: ThemeColorRow row })
            return;

        _pinnedInspectKey = string.Equals(_pinnedInspectKey, row.Key, StringComparison.Ordinal)
            ? null
            : row.Key;
        SyncMainRoleHighlight();
    }

    private void SyncMainRoleHighlight()
    {
        var key = _pinnedInspectKey ?? _hoverInspectKey;
        _highlightHost?.SetThemeRoleHighlight(key);
    }

    private static bool ShouldIgnoreInspectClick(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TextBox or Button)
                return true;
            if (current is FrameworkElement { Tag: string tag }
                && string.Equals(tag, "ThemeSwatch", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private void Slot_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ThemeSlotRow slot })
            SelectSlot(slot.Index);
    }

    private void LoadSlot_Click(object sender, RoutedEventArgs e)
    {
        var stored = _theme.LoadSlots();
        var palette = stored[_selectedSlotIndex];
        if (palette is null)
        {
            MessageBox.Show(this, "That slot is empty.", "Theme slots",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        PushUndoSnapshot(_lastApplied);

        _suppressLive = true;
        try
        {
            foreach (var row in _rows)
            {
                var hex = palette.TryGetValue(row.Key, out var h) ? h
                    : ThemeService.Roles.First(r => r.Key == row.Key).DefaultHex;
                row.SetHex(hex);
            }
        }
        finally
        {
            _suppressLive = false;
        }

        _theme.Apply(palette, persist: true);
        _lastApplied = CloneMap(CompleteFromRows());
        RefreshSwatchesFromApplied();
        UpdateUndoUi();
    }

    private void SaveSlot_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildMap(out var map, out var error))
        {
            MessageBox.Show(this, error, "Theme settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ApplyMap(map, persist: true);
        _theme.SaveSlot(_selectedSlotIndex, map);
        ReloadSlotsUi();
        SelectSlot(_selectedSlotIndex);
    }

    private void ClearSlot_Click(object sender, RoutedEventArgs e)
    {
        _theme.ClearSlot(_selectedSlotIndex);
        ReloadSlotsUi();
        SelectSlot(_selectedSlotIndex);
    }

    private void SelectSlot(int index)
    {
        _selectedSlotIndex = index;
        foreach (var slot in _slots)
            slot.IsSelected = slot.Index == index;
    }

    private void ReloadSlotsUi()
    {
        var stored = _theme.LoadSlots();
        for (var i = 0; i < _slots.Count; i++)
            _slots[i].SetPalette(stored[i]);
    }

    private void ApplyCurrent(bool persist)
    {
        if (!TryBuildMap(out var map, out _))
            return;
        ApplyMap(map, persist);
        ApplyFontsFromRows(persist: false);
    }

    private void ApplyMap(Dictionary<string, string> map, bool persist)
    {
        if (!MapsEqual(_lastApplied, map))
            PushUndoSnapshot(_lastApplied);

        _theme.Apply(map, ReadGlassFromUi(), persist || !_theme.ApplyToApplication);
        _lastApplied = CloneMap(map);
        RefreshSwatchesFromApplied();
        UpdateUndoUi();
    }

    private void PushUndoSnapshot(IReadOnlyDictionary<string, string> snapshot)
    {
        var clone = CloneMap(snapshot);
        if (_undoStack.Count > 0 && MapsEqual(_undoStack[^1], clone))
            return;

        _undoStack.Add(clone);
        while (_undoStack.Count > MaxUndoDepth)
            _undoStack.RemoveAt(0);
        UpdateUndoUi();
    }

    private void UpdateUndoUi() =>
        UndoButton.IsEnabled = _undoStack.Count > 0;

    private Dictionary<string, string> CompleteFromRows()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in _rows)
        {
            map[row.Key] = ThemeService.TryParseHex(row.HexText, out _)
                ? ThemeService.NormalizeHex(row.HexText)
                : ThemeService.Roles.First(r => r.Key == row.Key).DefaultHex;
        }
        return map;
    }

    private bool TryBuildMap(out Dictionary<string, string> map, out string error)
    {
        map = new Dictionary<string, string>(StringComparer.Ordinal);
        error = "";
        foreach (var row in _rows)
        {
            if (!ThemeService.TryParseHex(row.HexText, out _))
            {
                error = $"Invalid hex for {row.Purpose} ({row.Key}): '{row.HexText}'\nUse #RRGGBB or RRGGBB.";
                return false;
            }
            map[row.Key] = ThemeService.NormalizeHex(row.HexText);
        }
        return true;
    }

    private void RefreshSwatchesFromApplied()
    {
        _suppressLive = true;
        try
        {
            foreach (var row in _rows)
            {
                if (ThemeService.TryParseHex(row.HexText, out var c))
                    row.NotifySwatch(c);
            }
        }
        finally
        {
            _suppressLive = false;
        }
    }

    private static Dictionary<string, string> CloneMap(IReadOnlyDictionary<string, string> source)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var role in ThemeService.Roles)
        {
            if (source.TryGetValue(role.Key, out var hex) && ThemeService.TryParseHex(hex, out _))
                map[role.Key] = ThemeService.NormalizeHex(hex);
            else
                map[role.Key] = role.DefaultHex;
        }
        return map;
    }

    private static bool MapsEqual(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    {
        foreach (var role in ThemeService.Roles)
        {
            var ha = a.TryGetValue(role.Key, out var va) ? ThemeService.NormalizeHex(va) : role.DefaultHex;
            var hb = b.TryGetValue(role.Key, out var vb) ? ThemeService.NormalizeHex(vb) : role.DefaultHex;
            if (!string.Equals(ha, hb, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private void LoadGlassUiFromTheme()
    {
        _suppressGlassUi = true;
        try
        {
            var g = _theme.Glass;
            if (GlassEnabledCheck is not null)
                GlassEnabledCheck.IsChecked = g.Enabled;
            if (GlassIntensitySlider is not null)
                GlassIntensitySlider.Value = g.Intensity;
            if (GlassBlurSlider is not null)
                GlassBlurSlider.Value = g.BlurRadius;
            if (GlassHaloCheck is not null)
                GlassHaloCheck.IsChecked = g.Halo;
            if (GlassImagePathLabel is not null)
            {
                GlassImagePathLabel.Text = string.IsNullOrWhiteSpace(g.BackgroundImagePath)
                    ? "No image selected"
                    : g.BackgroundImagePath;
            }
            if (GlassIntensityLabel is not null)
                GlassIntensityLabel.Text = g.Intensity.ToString("0.00");
            if (GlassBlurLabel is not null)
                GlassBlurLabel.Text = ((int)g.BlurRadius).ToString();
        }
        catch (Exception ex)
        {
            LcHostLog.Error("LoadGlassUiFromTheme failed", ex);
        }
        finally
        {
            _suppressGlassUi = false;
        }
    }

    private GlassSettings ReadGlassFromUi()
    {
        return new GlassSettings
        {
            Enabled = GlassEnabledCheck?.IsChecked == true,
            BackgroundImagePath = _theme.Glass.BackgroundImagePath,
            Intensity = GlassIntensitySlider?.Value ?? 0.65,
            BlurRadius = GlassBlurSlider?.Value ?? 18,
            Halo = GlassHaloCheck?.IsChecked != false
        };
    }

    private void ApplyGlassLive()
    {
        if (!_chromeReady || _suppressGlassUi || _suppressLive || LivePreviewCheck?.IsChecked != true)
            return;
        if (!TryBuildMap(out var map, out _))
            return;
        try
        {
            LcHostLog.Info($"Going glass live apply enabled={ReadGlassFromUi().Enabled}");
            _theme.Apply(map, ReadGlassFromUi(), persist: false);
        }
        catch (Exception ex)
        {
            LcHostLog.Error("Going glass live apply failed", ex);
        }
    }

    private void GlassEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (!_chromeReady || _suppressGlassUi) return;
        LcHostLog.Info($"Going glass toggle IsChecked={GlassEnabledCheck?.IsChecked}");
        try
        {
            if (GlassEnabledCheck?.IsChecked == true
                && string.IsNullOrWhiteSpace(_theme.Glass.BackgroundImagePath))
            {
                MessageBox.Show(this,
                    "Choose a background image (png/jpg). Glass stays off until an image is set.",
                    "Going glass", MessageBoxButton.OK, MessageBoxImage.Information);
                _suppressGlassUi = true;
                if (GlassEnabledCheck is not null)
                    GlassEnabledCheck.IsChecked = false;
                _suppressGlassUi = false;
                return;
            }

            if (GlassEnabledCheck?.IsChecked == true)
            {
                var suggested = _theme.SuggestGlassPalette(CompleteFromRows());
                _suppressLive = true;
                try
                {
                    foreach (var row in _rows)
                    {
                        if (suggested.TryGetValue(row.Key, out var hex))
                            row.SetHex(hex);
                    }
                }
                finally
                {
                    _suppressLive = false;
                }
            }

            ApplyGlassLive();
        }
        catch (Exception ex)
        {
            LcHostLog.Error("Going glass toggle failed", ex);
            MessageBox.Show(this,
                "Going glass could not be applied. Details: " + LcHostLog.LogPath + "\n" + ex.Message,
                "Going glass", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void GlassSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_chromeReady || _suppressGlassUi) return;
        if (GlassIntensityLabel is not null && GlassIntensitySlider is not null)
            GlassIntensityLabel.Text = GlassIntensitySlider.Value.ToString("0.00");
        if (GlassBlurLabel is not null && GlassBlurSlider is not null)
            GlassBlurLabel.Text = ((int)GlassBlurSlider.Value).ToString();
        ApplyGlassLive();
    }

    private void GlassHalo_Changed(object sender, RoutedEventArgs e)
    {
        if (!_chromeReady || _suppressGlassUi) return;
        ApplyGlassLive();
    }

    private void ChooseGlassImage_Click(object sender, RoutedEventArgs e)
    {
        LcHostLog.Info("Going glass Choose background image");
        var dlg = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files|*.*",
            Title = "Choose glass background image"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var dest = _theme.PersistBackgroundImage(dlg.FileName);
            _theme.Glass.BackgroundImagePath = dest;
            if (GlassImagePathLabel is not null)
                GlassImagePathLabel.Text = dest;
            if (GlassEnabledCheck?.IsChecked != true)
            {
                _suppressGlassUi = true;
                if (GlassEnabledCheck is not null)
                    GlassEnabledCheck.IsChecked = true;
                _suppressGlassUi = false;
            }
            LcHostLog.Info($"Going glass image copied to {dest}");
            ApplyGlassLive();
        }
        catch (Exception ex)
        {
            LcHostLog.Error("Going glass image failed", ex);
            MessageBox.Show(this, ex.Message, "Going glass", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearGlassImage_Click(object sender, RoutedEventArgs e)
    {
        LcHostLog.Info("Going glass Clear image");
        _theme.Glass.BackgroundImagePath = null;
        if (GlassImagePathLabel is not null)
            GlassImagePathLabel.Text = "No image selected";
        _suppressGlassUi = true;
        if (GlassEnabledCheck is not null)
            GlassEnabledCheck.IsChecked = false;
        _suppressGlassUi = false;
        ApplyGlassLive();
    }

    private void SuggestGlassColors_Click(object sender, RoutedEventArgs e)
    {
        var suggested = _theme.SuggestGlassPalette(CompleteFromRows());
        _suppressLive = true;
        try
        {
            foreach (var row in _rows)
            {
                if (suggested.TryGetValue(row.Key, out var hex))
                    row.SetHex(hex);
            }
        }
        finally
        {
            _suppressLive = false;
        }
        ApplyGlassLive();
    }
}

public sealed class ThemeCategoryGroup
{
    public ThemeCategoryGroup(string name) => Name = name;

    public string Name { get; }
    public ObservableCollection<ThemeColorRow> Rows { get; } = new();
}

public sealed class ThemeSlotRow : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isEmpty = true;
    private ObservableCollection<Brush> _swatches = new();

    public ThemeSlotRow(int index) => Index = index;

    public int Index { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        private set
        {
            if (_isEmpty == value) return;
            _isEmpty = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasColors));
        }
    }

    public bool HasColors => !IsEmpty;

    public ObservableCollection<Brush> Swatches
    {
        get => _swatches;
        private set
        {
            _swatches = value;
            OnPropertyChanged();
        }
    }

    public void SetPalette(Dictionary<string, string>? palette)
    {
        if (palette is null || palette.Count == 0)
        {
            IsEmpty = true;
            Swatches = new ObservableCollection<Brush>();
            return;
        }

        IsEmpty = false;
        var brushes = new ObservableCollection<Brush>();
        foreach (var role in ThemeService.Roles)
        {
            var hex = palette.TryGetValue(role.Key, out var h) ? h : role.DefaultHex;
            brushes.Add(ThemeService.TryParseHex(hex, out var c)
                ? new SolidColorBrush(c)
                : new SolidColorBrush(Colors.Transparent));
        }
        Swatches = brushes;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ThemeColorRow : INotifyPropertyChanged
{
    private string _hexText;
    private Brush _swatchBrush;
    private bool _isHighlighted;

    public ThemeColorRow(string key, string purpose, string category, string categoryTintHex, string hex)
    {
        Key = key;
        Purpose = purpose;
        Category = category;
        CategoryTint = ThemeService.TryParseHex(categoryTintHex, out var tint)
            ? new SolidColorBrush(tint)
            : new SolidColorBrush(Colors.White);
        _hexText = hex;
        _swatchBrush = CreateSwatch(hex);
    }

    public string Key { get; }
    public string Purpose { get; }
    public string Category { get; }
    public Brush CategoryTint { get; }

    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (_isHighlighted == value) return;
            _isHighlighted = value;
            OnPropertyChanged();
        }
    }

    public string HexText
    {
        get => _hexText;
        set
        {
            if (_hexText == value) return;
            _hexText = value;
            OnPropertyChanged();
            if (ThemeService.TryParseHex(value, out var c))
            {
                SwatchBrush = new SolidColorBrush(c);
                HexChanged?.Invoke(this);
            }
        }
    }

    public Brush SwatchBrush
    {
        get => _swatchBrush;
        private set
        {
            _swatchBrush = value;
            OnPropertyChanged();
        }
    }

    public event Action<ThemeColorRow>? HexChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetHex(string hex)
    {
        _hexText = hex;
        OnPropertyChanged(nameof(HexText));
        if (ThemeService.TryParseHex(hex, out var c))
            SwatchBrush = new SolidColorBrush(c);
    }

    public void NotifySwatch(Color c) => SwatchBrush = new SolidColorBrush(c);

    private static Brush CreateSwatch(string hex) =>
        ThemeService.TryParseHex(hex, out var c)
            ? new SolidColorBrush(c)
            : new SolidColorBrush(Colors.Transparent);

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ThemeFontRoleRow : INotifyPropertyChanged
{
    private string _family;
    private double _size;

    public ThemeFontRoleRow(ThemeFontRole role, string family, double size, IReadOnlyList<string> families)
    {
        Key = role.Key;
        Title = role.Key switch
        {
            "Status" => "Service status",
            _ => role.Key
        };
        Purpose = role.Purpose;
        Families = families;
        _family = family;
        _size = ThemeService.ClampFontSize(size);
    }

    public string Key { get; }
    public string Title { get; }
    public string Purpose { get; }
    public IReadOnlyList<string> Families { get; }

    public string Family
    {
        get => _family;
        set
        {
            var next = ThemeService.ResolveFontFamily(value);
            if (string.Equals(_family, next, StringComparison.Ordinal))
                return;
            _family = next;
            OnPropertyChanged();
            FontChanged?.Invoke(this);
        }
    }

    public double Size
    {
        get => _size;
        set
        {
            var next = ThemeService.ClampFontSize(value);
            if (Math.Abs(_size - next) < 0.01)
                return;
            _size = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SizeLabel));
            FontChanged?.Invoke(this);
        }
    }

    public string SizeLabel => $"{_size:0} pt";

    public event Action<ThemeFontRoleRow>? FontChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Set(string family, double size)
    {
        _family = ThemeService.ResolveFontFamily(family);
        _size = ThemeService.ClampFontSize(size);
        OnPropertyChanged(nameof(Family));
        OnPropertyChanged(nameof(Size));
        OnPropertyChanged(nameof(SizeLabel));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
