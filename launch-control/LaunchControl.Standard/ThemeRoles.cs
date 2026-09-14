using System.Windows;

namespace LaunchControl.Standard;

/// <summary>
/// Attached property listing ThemeService color keys for click-to-identify mapping.
/// Comma-separated, e.g. "PrimaryActionColor,OnPrimaryActionColor".
/// </summary>
public static class ThemeRoles
{
    public static readonly DependencyProperty RolesProperty =
        DependencyProperty.RegisterAttached(
            "Roles",
            typeof(string),
            typeof(ThemeRoles),
            new FrameworkPropertyMetadata(null));

    public static void SetRoles(DependencyObject element, string? value) =>
        element.SetValue(RolesProperty, value);

    public static string? GetRoles(DependencyObject element) =>
        (string?)element.GetValue(RolesProperty);

    public static IReadOnlyList<string> Parse(string? roles)
    {
        if (string.IsNullOrWhiteSpace(roles))
            return [];

        return roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
