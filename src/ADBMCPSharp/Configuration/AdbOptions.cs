using System.ComponentModel.DataAnnotations;

namespace ADBMCPSharp.Configuration;

public sealed class AdbOptions
{
    public const string SectionName = "Adb";

    [Required]
    public string ExecutablePath { get; set; } = "adb";

    [Range(1, 120)]
    public int CommandTimeoutSeconds { get; set; } = 10;

    [Range(0, 5000)]
    public int VerificationDelayMilliseconds { get; set; } = 350;

    [MinLength(1)]
    public Dictionary<string, AdbServerOptions> Servers { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["local"] = new(),
    };

    public Dictionary<string, AdbDeviceOptions> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AdbServerOptions
{
    public AdbServerMode Mode { get; set; } = AdbServerMode.Local;
    public string? Host { get; set; }

    [Range(1, 65535)]
    public int Port { get; set; } = 5037;
}

public enum AdbServerMode
{
    Local,
    Remote,
}

public sealed class AdbDeviceOptions
{
    [Required]
    public string Server { get; set; } = "local";

    // The selector is server-side sensitive configuration and is never returned by a tool.
    [Required]
    public string Selector { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public DeviceCapabilityOverrides Capabilities { get; set; } = new();
    public Dictionary<string, AllowedAppOptions> AllowedApps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DeviceCapabilityOverrides
{
    public bool Enabled { get; set; } = true;
    public bool AllowPower { get; set; } = true;
    public bool AllowNavigation { get; set; } = true;
    public bool AllowAppLaunch { get; set; } = true;
    public bool AllowAppStop { get; set; }
}

public sealed class AllowedAppOptions
{
    [Required]
    public string Package { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}
