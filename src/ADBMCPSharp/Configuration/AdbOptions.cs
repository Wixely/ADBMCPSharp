using System.ComponentModel.DataAnnotations;

namespace ADBMCPSharp.Configuration;

public sealed class AdbOptions
{
    public const string SectionName = "Adb";

    [Required] public string ExecutablePath { get; set; } = "adb";
    [Range(1, 120)] public int CommandTimeoutSeconds { get; set; } = 10;
    [Range(0, 5000)] public int VerificationDelayMilliseconds { get; set; } = 350;
    [Range(1, 10)] public int AppLaunchVerificationAttempts { get; set; } = 6;
    [Range(1, 100)] public int MaxDiscoveryResults { get; set; } = 25;
    [Range(10, 300)] public int DiscoveryHandleLifetimeSeconds { get; set; } = 60;
    [Range(1, 1000)] public int MaxInstalledAppResults { get; set; } = 200;
    [Range(1, 2_147_483_647)] public int MaxApkBytes { get; set; } = 536_870_912;
    [Range(5, 600)] public int ApkDownloadTimeoutSeconds { get; set; } = 120;
    [Range(10, 1800)] public int PackageOperationTimeoutSeconds { get; set; } = 300;
    [Range(1, 600)] public int ArbitraryCommandTimeoutSeconds { get; set; } = 30;
    [Range(1, 120)] public int ConnectionOperationTimeoutSeconds { get; set; } = 15;
    [Range(1, 10)] public int ConnectionVerificationAttempts { get; set; } = 4;
    [Range(0, 5000)] public int ConnectionRetryDelayMilliseconds { get; set; } = 500;
    [Range(1, 64)] public int MaxArbitraryArgumentCount { get; set; } = 32;
    [Range(1, 4096)] public int MaxArbitraryArgumentLength { get; set; } = 1024;
    [Range(1, 32768)] public int MaxArbitraryTotalCharacters { get; set; } = 8192;

    [MinLength(1)]
    public Dictionary<string, AdbServerOptions> Servers { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["local"] = new(),
    };

    public Dictionary<string, AdbDeviceOptions> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ApkArtifactOptions> ApkArtifacts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AdbServerOptions
{
    public AdbServerMode Mode { get; set; } = AdbServerMode.Local;
    public string? Host { get; set; }
    [Range(1, 65535)] public int Port { get; set; } = 5037;
}

public enum AdbServerMode { Local, Remote }

public sealed class AdbDeviceOptions
{
    [Required] public string Server { get; set; } = "local";
    [Required] public string Selector { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DeviceCapabilityOverrides Capabilities { get; set; } = new();
    public Dictionary<string, AllowedAppOptions> AllowedApps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DeviceCapabilityOverrides
{
    public bool Enabled { get; set; } = true;
    public bool AllowInstalledAppListing { get; set; } = true;
    public bool AllowDiagnostics { get; set; } = true;
    public bool AllowMediaInspection { get; set; } = true;
    public bool AllowMediaMetadata { get; set; } = true;
    public bool AllowMediaControl { get; set; } = true;
    public bool AllowVolumeControl { get; set; } = true;
    public bool AllowPackageInstall { get; set; }
    public bool AllowPackageUninstall { get; set; }
    public bool AllowArbitraryCommands { get; set; }
    public bool AllowConnectionManagement { get; set; }
    public bool AllowPower { get; set; } = true;
    public bool AllowNavigation { get; set; } = true;
    public bool AllowAppLaunch { get; set; } = true;
    public bool AllowAppStop { get; set; }
}

public sealed class AllowedAppOptions
{
    [Required] public string Package { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool AllowUninstall { get; set; }
}

public sealed class ApkArtifactOptions
{
    [Required] public string Package { get; set; } = string.Empty;
    [Required] public string Source { get; set; } = string.Empty;
    [Required] public string Sha256 { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool AllowReplace { get; set; }
    public HashSet<string> AllowedDevices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
