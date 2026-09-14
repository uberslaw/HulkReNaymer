namespace LaunchControl.Standard.Theme;

public interface IThemeHighlightHost
{
    void SetThemeRoleHighlight(string? roleKey);
    void ClearThemeRoleHighlight();
}
