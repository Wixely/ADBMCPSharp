namespace ADBMCPSharp.Configuration;

public sealed class PolicyOptions
{
    public const string SectionName = "Policy";

    public bool InspectionEnabled { get; set; } = true;
    public bool PowerControlEnabled { get; set; }
    public bool NavigationControlEnabled { get; set; }
    public bool AppLaunchEnabled { get; set; }
    public bool AppStopEnabled { get; set; }
    public HashSet<NavigationAction> AllowedNavigationActions { get; set; } = [];
}

public enum NavigationAction
{
    Home,
    Back,
    Up,
    Down,
    Left,
    Right,
    Select,
    Menu,
    PlayPause,
    Next,
    Previous,
    VolumeUp,
    VolumeDown,
    Mute,
}
