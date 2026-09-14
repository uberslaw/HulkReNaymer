using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using LaunchControl.Standard.Theme;

namespace LaunchControl.Standard;

/// <summary>
/// Window/page root that paints an optional blurred background image + scrim under content.
/// </summary>
public sealed class GlassBackground : ContentControl
{
    private Image? _image;
    private Border? _scrim;
    private readonly BlurEffect _blur = new() { Radius = 0, RenderingBias = RenderingBias.Quality };

    static GlassBackground()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(GlassBackground),
            new FrameworkPropertyMetadata(typeof(GlassBackground)));
    }

    public GlassBackground()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _image = GetTemplateChild("PART_Image") as Image;
        _scrim = GetTemplateChild("PART_Scrim") as Border;
        if (_image is not null)
            _image.Effect = _blur;
        ApplyState(ThemeService.Current?.GlassState);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeService.ThemeChanged += OnThemeChanged;
        ApplyState(ThemeService.Current?.GlassState);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) =>
        ApplyState(ThemeService.Current?.GlassState);

    private void ApplyState(GlassState? state)
    {
        if (_image is null || _scrim is null)
            return;

        if (state is null || !state.Enabled || state.Background is null)
        {
            _image.Source = null;
            _image.Visibility = Visibility.Collapsed;
            _scrim.Visibility = Visibility.Collapsed;
            _blur.Radius = 0;
            return;
        }

        _image.Source = state.Background;
        _image.Visibility = Visibility.Visible;
        _blur.Radius = state.BlurRadius;
        _scrim.Visibility = Visibility.Visible;
        _scrim.Opacity = state.ScrimOpacity;
        _scrim.Background = Brushes.Black;
    }
}
