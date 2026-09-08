namespace G915Fix.Desktop;

/// <summary>Resource locations for hosts that embed the shared desktop UI.</summary>
public static class DesktopResources
{
    /// <summary>Include this in <c>Application.Styles</c> before displaying <c>DesktopMainView</c>.</summary>
    public static Uri ThemeUri { get; } = new("avares://G915Fix.Desktop/Themes/DesktopTheme.axaml");
}
