using ADBMCPSharp.Adb;
using ADBMCPSharp.Configuration;
using ADBMCPSharp.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ADBMCPSharp.Services;

public sealed class ArbitraryAdbService(
    DeviceInventory inventory,
    CapabilityPolicy policy,
    IAdbTransport transport,
    DeviceOperationCoordinator coordinator,
    IOptions<AdbOptions> options,
    ILogger<ArbitraryAdbService> logger)
{
    private static readonly HashSet<string> NonDeviceCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "connect", "disconnect", "devices", "help", "host-features", "keygen", "kill-server",
        "mdns", "nodaemon", "pair", "server", "start-server", "version",
    };

    private readonly AdbOptions _options = options.Value;

    public async Task<ArbitraryAdbResult> ExecuteAsync(
        string deviceAlias,
        IReadOnlyList<string>? arguments,
        bool confirmHighImpact,
        CancellationToken cancellationToken)
    {
        if (!inventory.TryGet(deviceAlias, out var device))
            return new(deviceAlias, OperationState.NotFound, null, "Unknown device alias.");
        if (!policy.ArbitraryCommands(device))
            return new(device.Alias, OperationState.Denied, null, "Arbitrary ADB commands are disabled for this device.");
        if (!confirmHighImpact)
            return new(device.Alias, OperationState.Denied, null, "Explicit high-impact confirmation is required.");
        if (!TryValidateArguments(arguments, _options, out var validationMessage))
            return new(device.Alias, OperationState.Denied, null, validationMessage);

        return await coordinator.WithLockAsync<ArbitraryAdbResult>(device.Alias, async token =>
        {
            var request = new AdbRequest(
                AdbRequestKind.ArbitraryDeviceCommand,
                TimeoutSeconds: _options.ArbitraryCommandTimeoutSeconds,
                Arguments: arguments);
            var result = await transport.ExecuteAsync(device.Server, device.Device.Selector, request, token);
            logger.LogWarning(
                "Break-glass arbitrary ADB request for device alias {DeviceAlias}: {Outcome}; argument count={ArgumentCount}",
                device.Alias,
                result.Success ? "accepted" : "failed",
                arguments!.Count);
            return result.Success
                ? new(device.Alias, OperationState.ObservedComplete, result.Output, null)
                : new(device.Alias, MapState(result), null, result.Message ?? "The arbitrary ADB request failed.");
        }, cancellationToken);
    }

    internal static bool TryValidateArguments(
        IReadOnlyList<string>? arguments,
        AdbOptions options,
        out string message)
    {
        if (arguments is not { Count: > 0 })
        {
            message = "At least one ADB device-command argument is required.";
            return false;
        }
        if (arguments.Count > options.MaxArbitraryArgumentCount)
        {
            message = "The configured argument-count limit was exceeded.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(arguments[0]) || arguments[0][0] == '-' || NonDeviceCommands.Contains(arguments[0]))
        {
            message = "The first argument must identify a device-scoped ADB operation; server and selector options are prohibited.";
            return false;
        }

        var totalCharacters = 0;
        foreach (var argument in arguments)
        {
            if (string.IsNullOrEmpty(argument) || argument.Length > options.MaxArbitraryArgumentLength || argument.Any(char.IsControl))
            {
                message = "Arguments must be non-empty, within the configured length limit, and contain no control characters.";
                return false;
            }
            totalCharacters += argument.Length;
            if (totalCharacters > options.MaxArbitraryTotalCharacters)
            {
                message = "The configured total argument-size limit was exceeded.";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    private static OperationState MapState(AdbExecutionResult result) => result.FailureKind switch
    {
        AdbFailureKind.TimedOut => OperationState.TimedOut,
        AdbFailureKind.Offline => OperationState.Offline,
        AdbFailureKind.Unauthorized => OperationState.Unauthorized,
        AdbFailureKind.Unavailable => OperationState.Indeterminate,
        _ => OperationState.Failed,
    };
}
