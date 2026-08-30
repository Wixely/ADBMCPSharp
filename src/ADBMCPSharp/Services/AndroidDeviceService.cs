using ADBMCPSharp.Adb;
using ADBMCPSharp.Configuration;
using ADBMCPSharp.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ADBMCPSharp.Services;

public sealed class AndroidDeviceService(
    DeviceInventory inventory,
    CapabilityPolicy policy,
    IAdbTransport transport,
    DeviceOperationCoordinator coordinator,
    IOptions<AdbOptions> options,
    ILogger<AndroidDeviceService> logger)
{
    private readonly AdbOptions _options = options.Value;

    public IReadOnlyList<DeviceSummary> ListDevices() => inventory.Aliases
        .Select(alias =>
        {
            inventory.TryGet(alias, out var configured);
            return new DeviceSummary(
                configured.Alias,
                string.IsNullOrWhiteSpace(configured.Device.DisplayName) ? configured.Alias : configured.Device.DisplayName,
                configured.Server.Mode.ToString(),
                configured.Device.Capabilities.Enabled);
        })
        .ToArray();

    public CapabilityStatus? GetCapabilities(string deviceAlias) =>
        inventory.TryGet(deviceAlias, out var device) ? policy.Describe(device) : null;

    public IReadOnlyList<AllowedApp>? ListAllowedApps(string deviceAlias)
    {
        if (!inventory.TryGet(deviceAlias, out var device)) return null;
        return device.Device.AllowedApps.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new AllowedApp(
                pair.Key,
                string.IsNullOrWhiteSpace(pair.Value.DisplayName) ? pair.Key : pair.Value.DisplayName,
                policy.AppLaunch(device),
                policy.AppStop(device),
                policy.PackageUninstall(device) && pair.Value.AllowUninstall))
            .ToArray();
    }

    public async Task<DeviceStatus> GetStatusAsync(string deviceAlias, CancellationToken cancellationToken)
    {
        if (!inventory.TryGet(deviceAlias, out var device))
            return new(deviceAlias, OperationState.NotFound, "unknown", null, null, null, null, null, null, null, "Unknown device alias.");
        if (!policy.Inspection(device))
            return new(device.Alias, OperationState.Denied, "not_inspected", null, null, null, null, null, null, null, "Inspection is disabled.");

        return await WithDeviceLockAsync(device, async token =>
        {
            var state = await ExecuteAsync(device, new(AdbRequestKind.GetState), token);
            if (!state.Success) return FailedStatus(device.Alias, state);

            var manufacturer = await ExecuteAsync(device, new(AdbRequestKind.GetManufacturer), token);
            var model = await ExecuteAsync(device, new(AdbRequestKind.GetModel), token);
            var version = await ExecuteAsync(device, new(AdbRequestKind.GetAndroidVersion), token);
            var api = await ExecuteAsync(device, new(AdbRequestKind.GetApiLevel), token);
            var power = await ExecuteAsync(device, new(AdbRequestKind.GetPowerState), token);
            var foreground = await ExecuteAsync(device, new(AdbRequestKind.GetForegroundWindow), token);
            var parsedPower = power.Success ? AndroidOutputParser.ParsePower(power.Output) : (null, null);

            return new DeviceStatus(
                device.Alias,
                OperationState.ObservedComplete,
                NormalizeConnectionState(state.Output),
                ValueOrNull(manufacturer),
                ValueOrNull(model),
                ValueOrNull(version),
                api.Success && int.TryParse(api.Output.Trim(), out var apiLevel) ? apiLevel : null,
                parsedPower.Item1,
                parsedPower.Item2,
                foreground.Success ? AndroidOutputParser.ParseForegroundPackage(foreground.Output) : null,
                power.Success && foreground.Success ? null : "Some optional device facts were unavailable.");
        }, cancellationToken);
    }

    public async Task<AppStatus> GetAppStatusAsync(string deviceAlias, string appAlias, CancellationToken cancellationToken)
    {
        if (!TryGetApp(deviceAlias, appAlias, out var device, out var app, out var failure))
            return new(deviceAlias, appAlias, failure.State, null, null, null, failure.Message);
        if (!policy.Inspection(device))
            return new(device.Alias, appAlias, OperationState.Denied, null, null, null, "Inspection is disabled.");

        return await WithDeviceLockAsync(device, async token =>
        {
            var installed = await ExecuteAsync(device, new(AdbRequestKind.GetPackagePath, app.Package), token);
            if (IsConnectionFailure(installed))
                return new AppStatus(device.Alias, appAlias, MapState(installed), null, null, null, installed.Message);
            if (!installed.Success)
                return new AppStatus(device.Alias, appAlias, OperationState.ObservedComplete, false, false, false, null);

            var process = await ExecuteAsync(device, new(AdbRequestKind.GetProcessId, app.Package), token);
            var foreground = await ExecuteAsync(device, new(AdbRequestKind.GetForegroundWindow), token);
            var foregroundPackage = foreground.Success ? AndroidOutputParser.ParseForegroundPackage(foreground.Output) : null;
            return new AppStatus(device.Alias, appAlias, OperationState.ObservedComplete, true, process.Success && process.Output.Length > 0,
                string.Equals(foregroundPackage, app.Package, StringComparison.Ordinal), null);
        }, cancellationToken);
    }

    public async Task<InstalledAppListResult> ListInstalledAppsAsync(
        string deviceAlias,
        InstalledAppScope scope,
        CancellationToken cancellationToken)
    {
        if (!inventory.TryGet(deviceAlias, out var device))
            return new(deviceAlias, OperationState.NotFound, scope.ToString(), 0, false, [], "Unknown device alias.");
        if (!policy.InstalledApps(device))
            return new(device.Alias, OperationState.Denied, scope.ToString(), 0, false, [], "Installed application listing is disabled.");

        return await WithDeviceLockAsync(device, async token =>
        {
            var result = await ExecuteAsync(device, new(AdbRequestKind.ListInstalledPackages, scope.ToString()), token);
            if (!result.Success)
                return new InstalledAppListResult(
                    device.Alias, MapState(result), scope.ToString(), 0, false, [], result.Message ?? "Installed application listing failed.");

            var packageNames = InstalledAppParser.Parse(result.Output, _options.MaxInstalledAppResults);
            var apps = packageNames.Select(packageName => new InstalledApp(packageName)).ToArray();
            var truncated = apps.Length == _options.MaxInstalledAppResults;
            return new InstalledAppListResult(
                device.Alias,
                OperationState.ObservedComplete,
                scope.ToString(),
                apps.Length,
                truncated,
                apps,
                truncated ? "The configured result limit was reached; additional packages may exist." : null);
        }, cancellationToken);
    }

    public Task<OperationResult> WakeAsync(string deviceAlias, CancellationToken cancellationToken) =>
        SetPowerAsync(deviceAlias, wake: true, cancellationToken);

    public Task<OperationResult> SleepAsync(string deviceAlias, CancellationToken cancellationToken) =>
        SetPowerAsync(deviceAlias, wake: false, cancellationToken);

    public async Task<OperationResult> NavigateAsync(string deviceAlias, NavigationAction action, CancellationToken cancellationToken)
    {
        if (!inventory.TryGet(deviceAlias, out var device)) return NotFound(deviceAlias);
        if (!policy.Navigation(device, action)) return Denied(device.Alias, "That navigation action is not enabled.");
        return await WithDeviceLockAsync(device, async token =>
        {
            var result = await ExecuteAsync(device, AdbRequest.Navigation(action), token);
            Audit(device.Alias, "navigation", result.Success);
            return ToOperation(device.Alias, result, "Navigation action was accepted by ADB.");
        }, cancellationToken);
    }

    public Task<OperationResult> LaunchAppAsync(string deviceAlias, string appAlias, CancellationToken cancellationToken) =>
        ChangeAppAsync(deviceAlias, appAlias, stop: false, cancellationToken);

    public Task<OperationResult> StopAppAsync(string deviceAlias, string appAlias, CancellationToken cancellationToken) =>
        ChangeAppAsync(deviceAlias, appAlias, stop: true, cancellationToken);

    public async Task<MediaStatusResult> GetMediaStatusAsync(string deviceAlias, CancellationToken cancellationToken)
    {
        if (!inventory.TryGet(deviceAlias, out var device))
            return new(deviceAlias, OperationState.NotFound, [], "Unknown device alias.");
        if (!policy.MediaInspection(device))
            return new(device.Alias, OperationState.Denied, [], "Media inspection is disabled.");

        return await WithDeviceLockAsync(device, async token =>
        {
            var result = await ExecuteAsync(device, new(AdbRequestKind.GetMediaSession), token);
            if (!result.Success)
                return new MediaStatusResult(device.Alias, MapState(result), [], result.Message);

            var parsed = MediaSessionParser.Parse(result.Output);
            var app = device.Device.AllowedApps.FirstOrDefault(candidate =>
                string.Equals(candidate.Value.Package, parsed.Package, StringComparison.Ordinal));
            var recognized = app.Key is not null;
            var sessions = recognized
                ? new[]
                {
                    new MediaSessionStatus(
                        app.Key!,
                        parsed.PlaybackState ?? "Unknown",
                        parsed.Active == true,
                        parsed.PositionMilliseconds,
                        parsed.Speed,
                        policy.MediaMetadata(device) ? parsed.Title : null,
                        policy.MediaMetadata(device) ? parsed.Artist : null,
                        policy.MediaMetadata(device) ? parsed.Album : null)
                }
                : [];
            var message = parsed.Package is null
                ? "No active media session was identified."
                : recognized ? null : "The active media session is not in the configured application allowlist and was redacted.";
            return new MediaStatusResult(device.Alias, OperationState.ObservedComplete, sessions, message);
        }, cancellationToken);
    }

    public async Task<OperationResult> SendMediaActionAsync(
        string deviceAlias,
        MediaAction action,
        CancellationToken cancellationToken)
    {
        if (!inventory.TryGet(deviceAlias, out var device)) return NotFound(deviceAlias);
        if (!policy.MediaControl(device, action)) return Denied(device.Alias, "That media action is not enabled.");
        return await WithDeviceLockAsync(device, async token =>
        {
            var result = await ExecuteAsync(device, new(AdbRequestKind.MediaAction, action.ToString()), token);
            Audit(device.Alias, "media_action", result.Success);
            return ToOperation(device.Alias, result, "Media action was accepted by ADB.");
        }, cancellationToken);
    }

    public async Task<OperationResult> SendVolumeActionAsync(
        string deviceAlias,
        VolumeAction action,
        CancellationToken cancellationToken)
    {
        if (!inventory.TryGet(deviceAlias, out var device)) return NotFound(deviceAlias);
        if (!policy.VolumeControl(device, action)) return Denied(device.Alias, "That volume action is not enabled.");
        return await WithDeviceLockAsync(device, async token =>
        {
            var result = await ExecuteAsync(device, AdbRequest.Volume(action), token);
            Audit(device.Alias, "volume_action", result.Success);
            return ToOperation(device.Alias, result, "Volume action was accepted by ADB.");
        }, cancellationToken);
    }

    private async Task<OperationResult> SetPowerAsync(string deviceAlias, bool wake, CancellationToken cancellationToken)
    {
        if (!inventory.TryGet(deviceAlias, out var device)) return NotFound(deviceAlias);
        if (!policy.Power(device)) return Denied(device.Alias, "Power control is disabled.");
        return await WithDeviceLockAsync(device, async token =>
        {
            var result = await ExecuteAsync(device, new(wake ? AdbRequestKind.Wake : AdbRequestKind.Sleep), token);
            Audit(device.Alias, wake ? "wake" : "sleep", result.Success);
            if (!result.Success) return ToOperation(device.Alias, result, "");
            await VerificationDelayAsync(token);
            var power = await ExecuteAsync(device, new(AdbRequestKind.GetPowerState), token);
            if (!power.Success) return new(device.Alias, OperationState.Accepted, "ADB accepted the request; postcondition could not be read.", false);
            var parsed = AndroidOutputParser.ParsePower(power.Output);
            var observed = wake ? parsed.Awake == true || parsed.DisplayOn == true : parsed.Awake == false || parsed.DisplayOn == false;
            return observed
                ? new(device.Alias, OperationState.ObservedComplete, wake ? "Device wake state was observed." : "Device sleep state was observed.", true)
                : new(device.Alias, OperationState.Accepted, "ADB accepted the request; the requested state was not yet observed.", false);
        }, cancellationToken);
    }

    private async Task<OperationResult> ChangeAppAsync(string deviceAlias, string appAlias, bool stop, CancellationToken cancellationToken)
    {
        if (!TryGetApp(deviceAlias, appAlias, out var device, out var app, out var failure)) return failure;
        if (stop ? !policy.AppStop(device) : !policy.AppLaunch(device))
            return Denied(device.Alias, stop ? "Application stopping is disabled." : "Application launching is disabled.");

        return await WithDeviceLockAsync(device, async token =>
        {
            var request = new AdbRequest(stop ? AdbRequestKind.StopPackage : AdbRequestKind.LaunchPackage, app.Package);
            var result = await ExecuteAsync(device, request, token);
            Audit(device.Alias, stop ? "stop_app" : "launch_app", result.Success);
            if (!result.Success) return ToOperation(device.Alias, result, "");
            await VerificationDelayAsync(token);
            var check = await ExecuteAsync(device,
                new(stop ? AdbRequestKind.GetProcessId : AdbRequestKind.GetForegroundWindow, stop ? app.Package : null), token);
            var observed = stop
                ? !check.Success && !IsConnectionFailure(check)
                : check.Success && string.Equals(AndroidOutputParser.ParseForegroundPackage(check.Output), app.Package, StringComparison.Ordinal);
            return observed
                ? new(device.Alias, OperationState.ObservedComplete, stop ? "Application stop was observed." : "Application became foreground.", true)
                : new(device.Alias, OperationState.Accepted, "ADB accepted the request; the postcondition was not yet observed.", false);
        }, cancellationToken);
    }

    private bool TryGetApp(string deviceAlias, string appAlias, out ConfiguredDevice device, out AllowedAppOptions app, out OperationResult failure)
    {
        if (!inventory.TryGet(deviceAlias, out device!))
        {
            app = default!;
            failure = NotFound(deviceAlias);
            return false;
        }
        var pair = device.Device.AllowedApps.FirstOrDefault(x => string.Equals(x.Key, appAlias, StringComparison.OrdinalIgnoreCase));
        if (pair.Key is null)
        {
            app = default!;
            failure = new(device.Alias, OperationState.NotFound, "Unknown application alias.");
            return false;
        }
        app = pair.Value;
        failure = default!;
        return true;
    }

    private Task<AdbExecutionResult> ExecuteAsync(ConfiguredDevice device, AdbRequest request, CancellationToken token) =>
        transport.ExecuteAsync(device.Server, device.Device.Selector, request, token);

    private async Task<T> WithDeviceLockAsync<T>(ConfiguredDevice device, Func<CancellationToken, Task<T>> action, CancellationToken token)
    {
        return await coordinator.WithLockAsync(device.Alias, action, token);
    }

    private Task VerificationDelayAsync(CancellationToken token) =>
        _options.VerificationDelayMilliseconds == 0 ? Task.CompletedTask : Task.Delay(_options.VerificationDelayMilliseconds, token);

    private void Audit(string alias, string operation, bool accepted) =>
        logger.LogInformation("Device control {Operation} for alias {DeviceAlias}: {Outcome}", operation, alias, accepted ? "accepted" : "failed");

    private static string NormalizeConnectionState(string state) => state.Trim().ToLowerInvariant() switch
    {
        "device" => "online",
        "offline" => "offline",
        "unauthorized" => "unauthorized",
        _ => "unknown",
    };

    private static DeviceStatus FailedStatus(string alias, AdbExecutionResult result) =>
        new(alias, MapState(result), result.FailureKind.ToString().ToLowerInvariant(), null, null, null, null, null, null, null, result.Message);

    private static string? ValueOrNull(AdbExecutionResult result) => result.Success && result.Output.Length > 0 ? result.Output.Trim() : null;
    private static bool IsConnectionFailure(AdbExecutionResult result) => result.FailureKind is AdbFailureKind.Offline or AdbFailureKind.Unauthorized or AdbFailureKind.Unavailable or AdbFailureKind.TimedOut;
    private static OperationState MapState(AdbExecutionResult result) => result.FailureKind switch
    {
        AdbFailureKind.TimedOut => OperationState.TimedOut,
        AdbFailureKind.Offline => OperationState.Offline,
        AdbFailureKind.Unauthorized => OperationState.Unauthorized,
        AdbFailureKind.Unavailable => OperationState.Indeterminate,
        _ => OperationState.Failed,
    };
    private static OperationResult ToOperation(string alias, AdbExecutionResult result, string successMessage) =>
        result.Success ? new(alias, OperationState.Accepted, successMessage) : new(alias, MapState(result), result.Message ?? "Operation failed.");
    private static OperationResult NotFound(string alias) => new(alias, OperationState.NotFound, "Unknown device alias.");
    private static OperationResult Denied(string alias, string message) => new(alias, OperationState.Denied, message);
}
