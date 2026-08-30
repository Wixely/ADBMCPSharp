using ADBMCPSharp.Configuration;

namespace ADBMCPSharp.Adb;

public interface IAdbTransport
{
    Task<AdbExecutionResult> ExecuteServerAsync(AdbServerOptions server, AdbServerRequest request, CancellationToken cancellationToken);
    Task<AdbExecutionResult> ExecuteAsync(AdbServerOptions server, string deviceSelector, AdbRequest request, CancellationToken cancellationToken);
}

public enum AdbServerRequest { CheckMdns, ListMdnsServices }

public enum AdbRequestKind
{
    GetState,
    GetManufacturer,
    GetModel,
    GetAndroidVersion,
    GetApiLevel,
    GetPowerState,
    GetForegroundWindow,
    GetPackagePath,
    GetProcessId,
    ListInstalledPackages,
    GetBatteryDiagnostic,
    GetMemoryDiagnostic,
    GetStorageDiagnostic,
    GetCpuLoadDiagnostic,
    GetRuntimeDiagnostic,
    GetDisplaySizeDiagnostic,
    GetDisplayDensityDiagnostic,
    GetBuildTypeDiagnostic,
    GetDebuggableDiagnostic,
    GetSecureDiagnostic,
    GetAdbSecureDiagnostic,
    GetVerifiedBootDiagnostic,
    GetFlashLockedDiagnostic,
    GetSelinuxDiagnostic,
    GetMediaSession,
    MediaAction,
    VolumeAction,
    InstallApk,
    UninstallPackage,
    ArbitraryDeviceCommand,
    Wake,
    Sleep,
    Navigation,
    LaunchPackage,
    StopPackage,
}

public sealed record AdbRequest(
    AdbRequestKind Kind,
    string? Value = null,
    bool Flag = false,
    int? TimeoutSeconds = null,
    IReadOnlyList<string>? Arguments = null)
{
    public static AdbRequest Navigation(NavigationAction action) => new(AdbRequestKind.Navigation, action.ToString());
    public static AdbRequest Media(MediaAction action) => new(AdbRequestKind.MediaAction, action.ToString());
    public static AdbRequest Volume(VolumeAction action) => new(AdbRequestKind.VolumeAction, action.ToString());
}

public enum AdbFailureKind { None, TimedOut, Cancelled, Offline, Unauthorized, Unavailable, Failed }

public sealed record AdbExecutionResult(
    bool Success,
    string Output,
    AdbFailureKind FailureKind = AdbFailureKind.None,
    string? Message = null);
