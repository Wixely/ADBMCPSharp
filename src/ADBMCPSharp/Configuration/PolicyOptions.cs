namespace ADBMCPSharp.Configuration;

public sealed class PolicyOptions
{
    public const string SectionName = "Policy";
    public bool InspectionEnabled { get; set; } = true;
    public bool DiscoveryEnabled { get; set; }
    public bool InstalledAppListingEnabled { get; set; }
    public bool DiagnosticsEnabled { get; set; }
    public bool MediaInspectionEnabled { get; set; }
    public bool MediaMetadataEnabled { get; set; }
    public bool MediaControlEnabled { get; set; }
    public bool VolumeControlEnabled { get; set; }
    public bool PackageInstallEnabled { get; set; }
    public bool PackageUninstallEnabled { get; set; }
    public bool ArbitraryCommandsEnabled { get; set; }
    public bool PowerControlEnabled { get; set; }
    public bool NavigationControlEnabled { get; set; }
    public bool AppLaunchEnabled { get; set; }
    public bool AppStopEnabled { get; set; }
    public HashSet<NavigationAction> AllowedNavigationActions { get; set; } = [];
    public HashSet<MediaAction> AllowedMediaActions { get; set; } = [];
    public HashSet<VolumeAction> AllowedVolumeActions { get; set; } = [];
    public HashSet<DiagnosticKind> AllowedDiagnostics { get; set; } = [];
}

public enum InstalledAppScope { All, User, System }
public enum DiagnosticKind { Battery, Memory, Storage, CpuLoad, Runtime, Display, Security }
public enum MediaAction { Play, Pause, PlayPause, Stop, Next, Previous, FastForward, Rewind }
public enum VolumeAction { Up, Down, Mute }
public enum NavigationAction
{
    Home, Back, Up, Down, Left, Right, Select, Menu,
}
