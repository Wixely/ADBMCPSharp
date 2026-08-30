namespace ADBMCPSharp.Models;

public enum OperationState
{
    ObservedComplete,
    Accepted,
    Failed,
    TimedOut,
    Offline,
    Unauthorized,
    Indeterminate,
    Denied,
    NotFound,
}

public sealed record DeviceSummary(
    string Alias,
    string DisplayName,
    string ServerMode,
    bool Enabled);

public sealed record DeviceStatus(
    string DeviceAlias,
    OperationState State,
    string ConnectionState,
    string? Manufacturer,
    string? Model,
    string? AndroidVersion,
    int? ApiLevel,
    bool? Awake,
    bool? DisplayOn,
    string? ForegroundPackage,
    string? Message);

public sealed record AllowedApp(
    string Alias,
    string DisplayName,
    bool LaunchEnabled,
    bool StopEnabled);

public sealed record AppStatus(
    string DeviceAlias,
    string AppAlias,
    OperationState State,
    bool? Installed,
    bool? Running,
    bool? Foreground,
    string? Message);

public sealed record CapabilityStatus(
    string DeviceAlias,
    bool Inspection,
    bool PowerControl,
    bool NavigationControl,
    bool AppLaunch,
    bool AppStop,
    IReadOnlyList<string> AllowedNavigationActions);

public sealed record OperationResult(
    string DeviceAlias,
    OperationState State,
    string Message,
    bool? Verified = null);
