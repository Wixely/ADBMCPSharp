using ADBMCPSharp.Configuration;

namespace ADBMCPSharp.Adb;

public interface IAdbTransport
{
    Task<AdbExecutionResult> ExecuteAsync(
        AdbServerOptions server,
        string deviceSelector,
        AdbRequest request,
        CancellationToken cancellationToken);
}

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
    Wake,
    Sleep,
    Navigation,
    LaunchPackage,
    StopPackage,
}

public sealed record AdbRequest(AdbRequestKind Kind, string? Value = null)
{
    public static AdbRequest Navigation(NavigationAction action) => new(AdbRequestKind.Navigation, action.ToString());
}

public enum AdbFailureKind
{
    None,
    TimedOut,
    Cancelled,
    Offline,
    Unauthorized,
    Unavailable,
    Failed,
}

public sealed record AdbExecutionResult(
    bool Success,
    string Output,
    AdbFailureKind FailureKind = AdbFailureKind.None,
    string? Message = null);
