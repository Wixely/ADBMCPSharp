using ADBMCPSharp.Adb;
using ADBMCPSharp.Configuration;
using ADBMCPSharp.Models;

namespace ADBMCPSharp.Services;

public sealed class DeviceDiagnosticService(
    DeviceInventory inventory,
    CapabilityPolicy policy,
    IAdbTransport transport,
    DeviceOperationCoordinator coordinator)
{
    private static readonly IReadOnlyDictionary<DiagnosticKind, string> Descriptions =
        new Dictionary<DiagnosticKind, string>
        {
            [DiagnosticKind.Battery] = "Charge, power source, health, temperature, and voltage.",
            [DiagnosticKind.Memory] = "Total, available, free, cached, buffered, and swap memory.",
            [DiagnosticKind.Storage] = "Aggregate /data capacity, usage, and available space.",
            [DiagnosticKind.CpuLoad] = "One-, five-, and fifteen-minute kernel load averages.",
            [DiagnosticKind.Runtime] = "Device uptime and aggregate CPU idle time.",
            [DiagnosticKind.Display] = "Physical and overridden display size and density.",
            [DiagnosticKind.Security] = "Build type, debug/security flags, verified boot, flash lock, and SELinux posture.",
        };

    public IReadOnlyList<DiagnosticOption>? List(string deviceAlias)
    {
        if (!inventory.TryGet(deviceAlias, out var device)) return null;
        return Descriptions.Select(pair =>
                new DiagnosticOption(pair.Key.ToString(), pair.Value, policy.Diagnostic(device, pair.Key)))
            .ToArray();
    }

    public async Task<DiagnosticResult> RunAsync(
        string deviceAlias,
        DiagnosticKind diagnostic,
        CancellationToken cancellationToken)
    {
        if (!inventory.TryGet(deviceAlias, out var device))
            return new(deviceAlias, diagnostic.ToString(), OperationState.NotFound, null, "Unknown device alias.");
        if (!policy.Diagnostic(device, diagnostic))
            return new(device.Alias, diagnostic.ToString(), OperationState.Denied, null, "That diagnostic is not enabled.");

        return await coordinator.WithLockAsync(device.Alias, token => RunCoreAsync(device, diagnostic, token), cancellationToken);
    }

    private async Task<DiagnosticResult> RunCoreAsync(
        ConfiguredDevice device,
        DiagnosticKind diagnostic,
        CancellationToken cancellationToken)
    {
        return diagnostic switch
        {
            DiagnosticKind.Battery => await RunSingleAsync(
                device, diagnostic, AdbRequestKind.GetBatteryDiagnostic, DiagnosticOutputParser.ParseBattery, cancellationToken),
            DiagnosticKind.Memory => await RunSingleAsync(
                device, diagnostic, AdbRequestKind.GetMemoryDiagnostic, DiagnosticOutputParser.ParseMemory, cancellationToken),
            DiagnosticKind.Storage => await RunSingleAsync(
                device, diagnostic, AdbRequestKind.GetStorageDiagnostic, DiagnosticOutputParser.ParseStorage, cancellationToken),
            DiagnosticKind.CpuLoad => await RunSingleAsync(
                device, diagnostic, AdbRequestKind.GetCpuLoadDiagnostic, DiagnosticOutputParser.ParseCpuLoad, cancellationToken),
            DiagnosticKind.Runtime => await RunSingleAsync(
                device, diagnostic, AdbRequestKind.GetRuntimeDiagnostic, DiagnosticOutputParser.ParseRuntime, cancellationToken),
            DiagnosticKind.Display => await RunDisplayAsync(device, diagnostic, cancellationToken),
            DiagnosticKind.Security => await RunSecurityAsync(device, diagnostic, cancellationToken),
            _ => new(device.Alias, diagnostic.ToString(), OperationState.NotFound, null, "Unknown diagnostic."),
        };
    }

    private async Task<DiagnosticResult> RunSingleAsync<T>(
        ConfiguredDevice device,
        DiagnosticKind diagnostic,
        AdbRequestKind requestKind,
        Func<string, T> parser,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(device, requestKind, cancellationToken);
        return result.Success
            ? Complete(device, diagnostic, parser(result.Output))
            : Failed(device, diagnostic, result);
    }

    private async Task<DiagnosticResult> RunDisplayAsync(
        ConfiguredDevice device,
        DiagnosticKind diagnostic,
        CancellationToken cancellationToken)
    {
        var size = await ExecuteAsync(device, AdbRequestKind.GetDisplaySizeDiagnostic, cancellationToken);
        if (!size.Success) return Failed(device, diagnostic, size);
        var density = await ExecuteAsync(device, AdbRequestKind.GetDisplayDensityDiagnostic, cancellationToken);
        return density.Success
            ? Complete(device, diagnostic, DiagnosticOutputParser.ParseDisplay(size.Output, density.Output))
            : Failed(device, diagnostic, density);
    }

    private async Task<DiagnosticResult> RunSecurityAsync(
        ConfiguredDevice device,
        DiagnosticKind diagnostic,
        CancellationToken cancellationToken)
    {
        var buildType = await ExecuteAsync(device, AdbRequestKind.GetBuildTypeDiagnostic, cancellationToken);
        if (!buildType.Success) return Failed(device, diagnostic, buildType);
        var debuggable = await ExecuteAsync(device, AdbRequestKind.GetDebuggableDiagnostic, cancellationToken);
        if (!debuggable.Success) return Failed(device, diagnostic, debuggable);
        var secure = await ExecuteAsync(device, AdbRequestKind.GetSecureDiagnostic, cancellationToken);
        if (!secure.Success) return Failed(device, diagnostic, secure);
        var adbSecure = await ExecuteAsync(device, AdbRequestKind.GetAdbSecureDiagnostic, cancellationToken);
        if (!adbSecure.Success) return Failed(device, diagnostic, adbSecure);
        var verifiedBoot = await ExecuteAsync(device, AdbRequestKind.GetVerifiedBootDiagnostic, cancellationToken);
        if (!verifiedBoot.Success) return Failed(device, diagnostic, verifiedBoot);
        var flashLocked = await ExecuteAsync(device, AdbRequestKind.GetFlashLockedDiagnostic, cancellationToken);
        if (!flashLocked.Success) return Failed(device, diagnostic, flashLocked);
        var selinux = await ExecuteAsync(device, AdbRequestKind.GetSelinuxDiagnostic, cancellationToken);
        if (!selinux.Success) return Failed(device, diagnostic, selinux);

        return Complete(device, diagnostic, new SecurityDiagnostic(
            DiagnosticOutputParser.ParseBuildType(buildType.Output),
            DiagnosticOutputParser.ParseBooleanProperty(debuggable.Output),
            DiagnosticOutputParser.ParseBooleanProperty(secure.Output),
            DiagnosticOutputParser.ParseBooleanProperty(adbSecure.Output),
            DiagnosticOutputParser.ParseVerifiedBootState(verifiedBoot.Output),
            DiagnosticOutputParser.ParseBooleanProperty(flashLocked.Output),
            DiagnosticOutputParser.ParseSelinux(selinux.Output)));
    }

    private Task<AdbExecutionResult> ExecuteAsync(
        ConfiguredDevice device,
        AdbRequestKind kind,
        CancellationToken cancellationToken) =>
        transport.ExecuteAsync(device.Server, device.Device.Selector, new(kind), cancellationToken);

    private static DiagnosticResult Complete(ConfiguredDevice device, DiagnosticKind diagnostic, object? data) =>
        new(device.Alias, diagnostic.ToString(), OperationState.ObservedComplete, data, null);

    private static DiagnosticResult Failed(ConfiguredDevice device, DiagnosticKind diagnostic, AdbExecutionResult result) =>
        new(device.Alias, diagnostic.ToString(), result.FailureKind switch
        {
            AdbFailureKind.TimedOut => OperationState.TimedOut,
            AdbFailureKind.Offline => OperationState.Offline,
            AdbFailureKind.Unauthorized => OperationState.Unauthorized,
            AdbFailureKind.Unavailable => OperationState.Indeterminate,
            _ => OperationState.Failed,
        }, null, result.Message ?? "The diagnostic could not be collected.");
}
