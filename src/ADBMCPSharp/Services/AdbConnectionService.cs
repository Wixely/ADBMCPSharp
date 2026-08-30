using ADBMCPSharp.Adb;
using ADBMCPSharp.Configuration;
using ADBMCPSharp.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ADBMCPSharp.Services;

public sealed class AdbConnectionService(
    DeviceInventory inventory,
    CapabilityPolicy policy,
    IAdbTransport transport,
    DeviceOperationCoordinator coordinator,
    IOptions<AdbOptions> options,
    ILogger<AdbConnectionService> logger)
{
    private readonly AdbOptions _options = options.Value;

    public async Task<ConnectionHealthResult> GetHealthAsync(string deviceAlias, CancellationToken cancellationToken)
    {
        if (!inventory.TryGet(deviceAlias, out var device))
            return new(deviceAlias, OperationState.NotFound, "unknown", false, false, "Unknown", "Unknown device alias.");
        if (!policy.Inspection(device))
            return new(device.Alias, OperationState.Denied, "disabled", false, false, device.Server.Mode.ToString(), "Device inspection is not enabled.");

        var result = await GetStateAsync(device, cancellationToken);
        return ToHealth(device, result);
    }

    public Task<ConnectionOperationResult> ConnectAsync(
        string deviceAlias, bool confirmChange, CancellationToken cancellationToken) =>
        ChangeAsync(deviceAlias, AdbConnectionRequest.Connect, confirmChange, cancellationToken);

    public Task<ConnectionOperationResult> DisconnectAsync(
        string deviceAlias, bool confirmChange, CancellationToken cancellationToken) =>
        ChangeAsync(deviceAlias, AdbConnectionRequest.Disconnect, confirmChange, cancellationToken);

    public async Task<ConnectionOperationResult> ReconnectAsync(
        string deviceAlias, bool confirmChange, CancellationToken cancellationToken)
    {
        if (!TryAuthorize(deviceAlias, confirmChange, out var device, out var denied)) return denied!;

        return await coordinator.WithLockAsync(device.Alias, async token =>
        {
            var disconnected = await transport.ExecuteConnectionAsync(
                device.Server, device.Device.Selector, AdbConnectionRequest.Disconnect, token);
            if (!disconnected.Success && disconnected.FailureKind is AdbFailureKind.TimedOut or AdbFailureKind.Cancelled)
                return FromFailure(device.Alias, disconnected, "unknown", "ADB did not complete the disconnect phase.");

            await RetryDelayAsync(token);
            var connected = await transport.ExecuteConnectionAsync(
                device.Server, device.Device.Selector, AdbConnectionRequest.Connect, token);
            Audit(device.Alias, "reconnect", connected.Success);
            if (!connected.Success) return FromFailure(device.Alias, connected, ToConnectionState(connected), connected.Message);

            return await VerifyAsync(device, expectOnline: true, "ADB accepted the reconnect request.", token);
        }, cancellationToken);
    }

    private async Task<ConnectionOperationResult> ChangeAsync(
        string deviceAlias, AdbConnectionRequest request, bool confirmChange, CancellationToken cancellationToken)
    {
        if (!TryAuthorize(deviceAlias, confirmChange, out var device, out var denied)) return denied!;

        return await coordinator.WithLockAsync(device.Alias, async token =>
        {
            var before = await GetStateAsync(device, token);
            if (request == AdbConnectionRequest.Connect && before.Success)
                return new(device.Alias, OperationState.ObservedComplete, "online", "Device was already connected.", true);
            if (request == AdbConnectionRequest.Disconnect && IsAbsent(before))
                return new(device.Alias, OperationState.ObservedComplete, ToConnectionState(before), "Device was already disconnected.", true);

            var result = await transport.ExecuteConnectionAsync(device.Server, device.Device.Selector, request, token);
            Audit(device.Alias, request.ToString().ToLowerInvariant(), result.Success);
            if (!result.Success) return FromFailure(device.Alias, result, ToConnectionState(result), result.Message);

            return await VerifyAsync(
                device,
                request == AdbConnectionRequest.Connect,
                $"ADB accepted the {request.ToString().ToLowerInvariant()} request.",
                token);
        }, cancellationToken);
    }

    private bool TryAuthorize(
        string deviceAlias, bool confirmChange, out ConfiguredDevice device, out ConnectionOperationResult? denied)
    {
        if (!inventory.TryGet(deviceAlias, out device!))
        {
            denied = new(deviceAlias, OperationState.NotFound, "unknown", "Unknown device alias.");
            return false;
        }
        if (!policy.ConnectionManagement(device))
        {
            denied = new(device.Alias, OperationState.Denied, "unknown", "Connection management is not enabled for this device.");
            return false;
        }
        if (!confirmChange)
        {
            denied = new(device.Alias, OperationState.Denied, "unknown", "Explicit connection-change confirmation is required.");
            return false;
        }
        denied = null;
        return true;
    }

    private async Task<ConnectionOperationResult> VerifyAsync(
        ConfiguredDevice device, bool expectOnline, string acceptedMessage, CancellationToken token)
    {
        AdbExecutionResult? observed = null;
        for (var attempt = 0; attempt < _options.ConnectionVerificationAttempts; attempt++)
        {
            if (attempt > 0) await RetryDelayAsync(token);
            observed = await GetStateAsync(device, token);
            if ((expectOnline && observed.Success) || (!expectOnline && IsAbsent(observed)))
                return new(
                    device.Alias,
                    OperationState.ObservedComplete,
                    expectOnline ? "online" : ToConnectionState(observed),
                    expectOnline ? "Device connection was observed." : "Device disconnection was observed.",
                    true);
            if (observed.FailureKind is AdbFailureKind.Unauthorized or AdbFailureKind.Cancelled) break;
        }

        return new(
            device.Alias,
            OperationState.Accepted,
            observed is null ? "unknown" : ToConnectionState(observed),
            acceptedMessage + " The requested state could not be verified.",
            false);
    }

    private Task<AdbExecutionResult> GetStateAsync(ConfiguredDevice device, CancellationToken token) =>
        transport.ExecuteAsync(device.Server, device.Device.Selector, new(AdbRequestKind.GetState), token);

    private Task RetryDelayAsync(CancellationToken token) =>
        _options.ConnectionRetryDelayMilliseconds == 0
            ? Task.CompletedTask
            : Task.Delay(_options.ConnectionRetryDelayMilliseconds, token);

    private static bool IsAbsent(AdbExecutionResult result) =>
        !result.Success && result.FailureKind is AdbFailureKind.Offline or AdbFailureKind.Unavailable;

    private static ConnectionHealthResult ToHealth(ConfiguredDevice device, AdbExecutionResult result) =>
        result.Success
            ? new(device.Alias, OperationState.ObservedComplete, "online", true, true, device.Server.Mode.ToString(), null)
            : new(
                device.Alias,
                ToOperationState(result),
                ToConnectionState(result),
                result.FailureKind == AdbFailureKind.Unauthorized,
                false,
                device.Server.Mode.ToString(),
                result.Message);

    private static ConnectionOperationResult FromFailure(
        string alias, AdbExecutionResult result, string connectionState, string? fallback) =>
        new(alias, ToOperationState(result), connectionState, result.Message ?? fallback ?? "Connection operation failed.");

    private static OperationState ToOperationState(AdbExecutionResult result) => result.FailureKind switch
    {
        AdbFailureKind.TimedOut => OperationState.TimedOut,
        AdbFailureKind.Offline => OperationState.Offline,
        AdbFailureKind.Unauthorized => OperationState.Unauthorized,
        AdbFailureKind.Unavailable => OperationState.Indeterminate,
        AdbFailureKind.Cancelled => OperationState.Indeterminate,
        _ => OperationState.Failed,
    };

    private static string ToConnectionState(AdbExecutionResult result) => result.Success ? "online" : result.FailureKind switch
    {
        AdbFailureKind.Offline => "offline",
        AdbFailureKind.Unauthorized => "unauthorized",
        AdbFailureKind.Unavailable => "unavailable",
        AdbFailureKind.TimedOut => "unknown",
        _ => "unknown",
    };

    private void Audit(string deviceAlias, string operation, bool accepted) =>
        logger.LogInformation("ADB connection operation {Operation} for device {DeviceAlias}: {Outcome}",
            operation, deviceAlias, accepted ? "accepted" : "failed");
}
