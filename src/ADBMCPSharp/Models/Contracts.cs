namespace ADBMCPSharp.Models;

public enum OperationState { ObservedComplete, Accepted, Failed, TimedOut, Offline, Unauthorized, Indeterminate, Denied, NotFound }

public sealed record DeviceSummary(string Alias, string DisplayName, string ServerMode, bool Enabled);
public sealed record AdbServerSummary(string Alias, string Mode, bool DiscoveryEnabled);

public sealed record DeviceStatus(
    string DeviceAlias, OperationState State, string ConnectionState, string? Manufacturer, string? Model,
    string? AndroidVersion, int? ApiLevel, bool? Awake, bool? DisplayOn, string? ForegroundPackage, string? Message);

public sealed record AllowedApp(
    string Alias, string DisplayName, bool LaunchEnabled, bool StopEnabled, bool UninstallEnabled);

public sealed record AppStatus(
    string DeviceAlias, string AppAlias, OperationState State, bool? Installed, bool? Running, bool? Foreground, string? Message);

public sealed record CapabilityStatus(
    string DeviceAlias,
    bool Inspection,
    bool Diagnostics,
    bool InstalledAppListing,
    bool MediaInspection,
    bool MediaMetadata,
    bool MediaControl,
    bool VolumeControl,
    bool PackageInstall,
    bool PackageUninstall,
    bool ArbitraryCommands,
    bool ConnectionManagement,
    bool PowerControl,
    bool NavigationControl,
    bool AppLaunch,
    bool AppStop,
    IReadOnlyList<string> AllowedDiagnostics,
    IReadOnlyList<string> AllowedNavigationActions,
    IReadOnlyList<string> AllowedMediaActions,
    IReadOnlyList<string> AllowedVolumeActions);

public sealed record DiagnosticOption(string Name, string Description, bool Enabled);
public sealed record DiagnosticResult(
    string DeviceAlias, string Diagnostic, OperationState State, object? Data, string? Message);
public sealed record BatteryDiagnostic(
    int? LevelPercent, string? Status, string? Health, bool AcPowered, bool UsbPowered,
    bool WirelessPowered, int? TemperatureCelsiusTenths, int? VoltageMillivolts);
public sealed record MemoryDiagnostic(
    long? TotalKilobytes, long? AvailableKilobytes, long? FreeKilobytes, long? BuffersKilobytes,
    long? CachedKilobytes, long? SwapTotalKilobytes, long? SwapFreeKilobytes);
public sealed record StorageDiagnostic(
    long? TotalKilobytes, long? UsedKilobytes, long? AvailableKilobytes, int? UsedPercent);
public sealed record CpuLoadDiagnostic(double? OneMinute, double? FiveMinutes, double? FifteenMinutes);
public sealed record RuntimeDiagnostic(double? UptimeSeconds, double? IdleSeconds);
public sealed record DisplayDiagnostic(
    int? PhysicalWidth, int? PhysicalHeight, int? OverrideWidth, int? OverrideHeight,
    int? PhysicalDensityDpi, int? OverrideDensityDpi);
public sealed record SecurityDiagnostic(
    string? BuildType, bool? Debuggable, bool? Secure, bool? AdbSecure,
    string? VerifiedBootState, bool? FlashLocked, bool? SelinuxEnforcing);

public sealed record InstalledAppListResult(
    string DeviceAlias, OperationState State, string Scope, int Count, bool Truncated,
    IReadOnlyList<InstalledApp> Apps, string? Message);
public sealed record InstalledApp(string PackageName);

public sealed record MediaStatusResult(
    string DeviceAlias, OperationState State, IReadOnlyList<MediaSessionStatus> Sessions, string? Message);

public sealed record MediaSessionStatus(
    string AppAlias, string PlaybackState, bool? Active, long? PositionMilliseconds, double? Speed,
    string? Title, string? Artist, string? Album);

public sealed record ApkArtifactSummary(
    string Alias, string DisplayName, bool AllowReplace, bool AllowedForDevice);

public sealed record PackageOperationResult(
    string DeviceAlias, string ItemAlias, OperationState State, string Message, bool? Verified = null);

public sealed record ArbitraryAdbResult(
    string DeviceAlias, OperationState State, string? Output, string? Message);

public sealed record OperationResult(string DeviceAlias, OperationState State, string Message, bool? Verified = null);

public sealed record ConnectionHealthResult(
    string DeviceAlias, OperationState State, string ConnectionState, bool Reachable, bool Authorized,
    string ServerMode, string? Message);

public sealed record ConnectionOperationResult(
    string DeviceAlias, OperationState State, string ConnectionState, string Message, bool? Verified = null);

public sealed record DiscoveryResult(
    string ServerAlias, OperationState State, bool MdnsAvailable, int AdvertisementCount,
    IReadOnlyList<DiscoveredAdbService> Advertisements, string? Message);

public sealed record DiscoveredAdbService(
    string DiscoveryHandle, string Label, string ServiceType, bool PairingWindowOpen, DateTimeOffset ExpiresAtUtc);
