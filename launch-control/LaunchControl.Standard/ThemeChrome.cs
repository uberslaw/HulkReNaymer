using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using LaunchControl.Standard.Theme;

namespace LaunchControl.Standard;

/// <summary>
/// Attached glass helpers: text halo and frosted card/button overlay opacity.
/// </summary>
public static class ThemeChrome
{
    public static readonly DependencyProperty HaloProperty =
        DependencyProperty.RegisterAttached(
            "Halo",
            typeof(bool),
            typeof(ThemeChrome),
            new PropertyMetadata(false, OnHaloChanged));

    public static void SetHalo(DependencyObject element, bool value) =>
        element.SetValue(HaloProperty, value);

    public static bool GetHalo(DependencyObject element) =>
        (bool)element.GetValue(HaloProperty);

    private static void OnHaloChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el)
            return;

        if (e.NewValue is true)
        {
            ApplyHalo(el);
            EventHandler handler = (_, _) => ApplyHalo(el);
            el.SetValue(HaloHandlerProperty, handler);
            ThemeService.ThemeChanged += handler;
        }
        else
        {
            if (el.GetValue(HaloHandlerProperty) is EventHandler handler)
                ThemeService.ThemeChanged -= handler;
            el.ClearValue(UIElement.EffectProperty);
        }
    }

    private static readonly DependencyProperty HaloHandlerProperty =
        DependencyProperty.RegisterAttached(
            "HaloHandler",
            typeof(EventHandler),
            typeof(ThemeChrome));

    private static void ApplyHalo(UIElement el)
    {
        var state = ThemeService.Current?.GlassState;
        var opacity = state?.HaloOpacity ?? 0;
        if (opacity <= 0.01)
        {
            el.ClearValue(UIElement.EffectProperty);
            return;
        }

        var haloColor = PickHaloColor();
        if (el.Effect is DropShadowEffect existing && !existing.IsFrozen)
        {
            existing.Color = haloColor;
            existing.BlurRadius = 8;
            existing.ShadowDepth = 0;
            existing.Opacity = opacity;
            return;
        }

        el.Effect = new DropShadowEffect
        {
            Color = haloColor,
            BlurRadius = 8,
            ShadowDepth = 0,
            Opacity = opacity,
            RenderingBias = RenderingBias.Performance
        };
    }

    private static Color PickHaloColor()
    {
        var map = ThemeService.Current?.GetEffectiveHexMap();
        if (map is not null
            && map.TryGetValue("BodyTextColor", out var hex)
            && ThemeService.TryParseHex(hex, out var text))
        {
            var lum = RelativeLuminance(text);
            return lum > 0.55 ? Color.FromRgb(16, 16, 16) : Colors.White;
        }

        return Colors.White;
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
}
