using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HulkReNaymer.App.ViewModels;
using Microsoft.Win32;

namespace HulkReNaymer.App;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = new();
    bool _suppress;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.RequestCollectRules += PullRules;
        _vm.RulesChanged += PushRules;
        RulesHost.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(OnRulesChanged));
        RulesHost.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler(OnRulesChanged));
        RulesHost.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler(OnRulesChanged));
        RulesHost.AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler(OnRulesChanged));
        Loaded += (_, _) => _vm.OpenPath(_vm.CurrentPath);
    }

    void OnRulesChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        PullRules();
        _vm.Refresh();
    }

    void OnScanChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _vm.Refresh();
    }

    void PathKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            _vm.OpenPath(PathBox.Text);
    }

    void PlaceClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
            _vm.OpenPath(path);
    }

    void BrowseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder to rename", InitialDirectory = _vm.CurrentPath };
        if (dialog.ShowDialog() == true)
            _vm.OpenPath(dialog.FolderName);
    }

    void PresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || PresetBox.SelectedItem is not string name || string.IsNullOrEmpty(name))
            return;
        _vm.ApplyPreset(name);
        PushRules();
    }

    void FavoriteChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string name })
        {
            _vm.LoadFavorite(name);
            PushRules();
        }
    }

    void ResetClicked(object sender, RoutedEventArgs e)
    {
        _vm.ResetRules();
        PushRules();
    }

    void ImportMappingClicked(object sender, RoutedEventArgs e)
    {
        _vm.MappingText = MappingBox.Text;
        PullRules();
        _vm.ImportMapping();
    }

    void PullRules()
    {
        var rules = _vm.Rules;
        rules.RegexEnabled = RegexOn.IsChecked == true;
        rules.RegexPattern = RegexPattern.Text;
        rules.RegexReplace = RegexReplace.Text;
        rules.RegexApplyTo = Combo(RegexApplyTo);
        rules.NameEnabled = NameOn.IsChecked == true;
        rules.NameMode = Combo(NameMode);
        rules.NameFixed = NameFixed.Text;
        rules.ExtMode = Combo(ExtMode);
        rules.ExtFixed = ExtFixed.Text;
        rules.ReplaceEnabled = ReplaceOn.IsChecked == true;
        rules.Find = FindBox.Text;
        rules.ReplaceWith = ReplaceWithBox.Text;
        rules.ReplaceAll = ReplaceAllBox.IsChecked != false;
        rules.ReplaceCaseSensitive = ReplaceCaseBox.IsChecked != false;
        rules.CaseMode = Combo(CaseModeBox);
        rules.CaseApplyTo = Combo(CaseApplyToBox);
        rules.RemoveEnabled = RemoveOn.IsChecked == true;
        rules.RemoveFirstN = Int(RemoveFirst.Text);
        rules.RemoveLastN = Int(RemoveLast.Text);
        rules.RemoveChars = RemoveChars.Text;
        rules.RemoveDigits = RemoveDigits.IsChecked == true;
        rules.RemoveSymbols = RemoveSymbols.IsChecked == true;
        rules.CollapseSpaces = CollapseSpaces.IsChecked == true;
        rules.RemoveTrim = RemoveTrim.IsChecked == true;
        rules.MoveEnabled = MoveOn.IsChecked == true;
        rules.MoveFrom = Int(MoveFrom.Text);
        rules.MoveCount = Int(MoveCount.Text);
        rules.MoveTo = Int(MoveTo.Text);
        rules.MoveCopy = MoveCopy.IsChecked == true;
        rules.AddEnabled = AddOn.IsChecked == true;
        rules.Prefix = PrefixBox.Text;
        rules.Suffix = SuffixBox.Text;
        rules.Insert = InsertBox.Text;
        rules.InsertAt = Int(InsertAtBox.Text);
        rules.FolderEnabled = FolderOn.IsChecked == true;
        rules.FolderMode = Combo(FolderModeBox);
        rules.FolderSeparator = FolderSep.Text;
        rules.NumberingEnabled = NumberOn.IsChecked == true;
        rules.NumberStart = Int(NumberStart.Text, 1);
        rules.NumberIncrement = Int(NumberStep.Text, 1);
        rules.NumberPad = Int(NumberPad.Text, 3);
        rules.NumberPosition = Combo(NumberPos);
        rules.NumberSeparator = NumberSep.Text;
        rules.NumberResetPerFolder = NumberReset.IsChecked == true;
        rules.DateEnabled = DateOn.IsChecked == true;
        rules.DateSource = Combo(DateSourceBox);
        rules.DateFormat = DateFormatBox.Text;
        rules.DatePosition = Combo(DatePosBox);
        rules.DateSeparator = DateSep.Text;
        rules.JsEnabled = JsOn.IsChecked == true;
        rules.JsCode = JsBox.Text;
        rules.MappingExclusive = MappingExclusive.IsChecked != false;
        rules.Operation = Combo(OperationBox);
        rules.DestDir = DestDirBox.Text;
        rules.SetTimestamps = SetTimestamps.IsChecked == true;
        rules.TsModified = TsModified.Text;
        rules.TsAccessed = TsAccessed.Text;
        rules.SetReadonly = SetReadonly.IsChecked == true;
        rules.ReadonlyValue = ReadonlyValue.IsChecked == true;
        rules.SetHidden = SetHidden.IsChecked == true;
        rules.HiddenValue = HiddenValue.IsChecked == true;
        rules.WindowsSafe = WindowsSafe.IsChecked != false;
        _vm.Wildcard = WildcardBox.Text;
        _vm.MappingText = MappingBox.Text;
    }

    void PushRules()
    {
        _suppress = true;
        try
        {
            var rules = _vm.Rules;
            RegexOn.IsChecked = rules.RegexEnabled;
            RegexPattern.Text = rules.RegexPattern;
            RegexReplace.Text = rules.RegexReplace;
            SetCombo(RegexApplyTo, rules.RegexApplyTo);
            NameOn.IsChecked = rules.NameEnabled;
            SetCombo(NameMode, rules.NameMode);
            NameFixed.Text = rules.NameFixed;
            SetCombo(ExtMode, rules.ExtMode);
            ExtFixed.Text = rules.ExtFixed;
            ReplaceOn.IsChecked = rules.ReplaceEnabled;
            FindBox.Text = rules.Find;
            ReplaceWithBox.Text = rules.ReplaceWith;
            ReplaceAllBox.IsChecked = rules.ReplaceAll;
            ReplaceCaseBox.IsChecked = rules.ReplaceCaseSensitive;
            SetCombo(CaseModeBox, rules.CaseMode);
            SetCombo(CaseApplyToBox, rules.CaseApplyTo);
            RemoveOn.IsChecked = rules.RemoveEnabled;
            RemoveFirst.Text = rules.RemoveFirstN.ToString();
            RemoveLast.Text = rules.RemoveLastN.ToString();
            RemoveChars.Text = rules.RemoveChars;
            RemoveDigits.IsChecked = rules.RemoveDigits;
            RemoveSymbols.IsChecked = rules.RemoveSymbols;
            CollapseSpaces.IsChecked = rules.CollapseSpaces;
            RemoveTrim.IsChecked = rules.RemoveTrim;
            MoveOn.IsChecked = rules.MoveEnabled;
            MoveFrom.Text = rules.MoveFrom.ToString();
            MoveCount.Text = rules.MoveCount.ToString();
            MoveTo.Text = rules.MoveTo.ToString();
            MoveCopy.IsChecked = rules.MoveCopy;
            AddOn.IsChecked = rules.AddEnabled;
            PrefixBox.Text = rules.Prefix;
            SuffixBox.Text = rules.Suffix;
            InsertBox.Text = rules.Insert;
            InsertAtBox.Text = rules.InsertAt.ToString();
            FolderOn.IsChecked = rules.FolderEnabled;
            SetCombo(FolderModeBox, rules.FolderMode);
            FolderSep.Text = rules.FolderSeparator;
            NumberOn.IsChecked = rules.NumberingEnabled;
            NumberStart.Text = rules.NumberStart.ToString();
            NumberStep.Text = rules.NumberIncrement.ToString();
            NumberPad.Text = rules.NumberPad.ToString();
            SetCombo(NumberPos, rules.NumberPosition);
            NumberSep.Text = rules.NumberSeparator;
            NumberReset.IsChecked = rules.NumberResetPerFolder;
            DateOn.IsChecked = rules.DateEnabled;
            SetCombo(DateSourceBox, rules.DateSource);
            DateFormatBox.Text = rules.DateFormat;
            SetCombo(DatePosBox, rules.DatePosition);
            DateSep.Text = rules.DateSeparator;
            JsOn.IsChecked = rules.JsEnabled;
            JsBox.Text = rules.JsCode;
            MappingExclusive.IsChecked = rules.MappingExclusive;
            SetCombo(OperationBox, rules.Operation);
            DestDirBox.Text = rules.DestDir;
            SetTimestamps.IsChecked = rules.SetTimestamps;
            TsModified.Text = rules.TsModified;
            TsAccessed.Text = rules.TsAccessed;
            SetReadonly.IsChecked = rules.SetReadonly;
            ReadonlyValue.IsChecked = rules.ReadonlyValue;
            SetHidden.IsChecked = rules.SetHidden;
            HiddenValue.IsChecked = rules.HiddenValue;
            WindowsSafe.IsChecked = rules.WindowsSafe;
        }
        finally
        {
            _suppress = false;
        }
    }

    static string Combo(ComboBox box)
    {
        if (box.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString() ?? item.Content?.ToString() ?? "";
        return "";
    }

    static void SetCombo(ComboBox box, string value)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            var key = item.Tag?.ToString() ?? item.Content?.ToString() ?? "";
            if (string.Equals(key, value, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    static int Int(string text, int fallback = 0) =>
        int.TryParse(text, out var value) ? value : fallback;
}
