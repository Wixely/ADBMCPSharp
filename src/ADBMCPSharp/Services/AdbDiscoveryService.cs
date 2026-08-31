using System.Security.Cryptography;
using ADBMCPSharp.Adb;
using ADBMCPSharp.Configuration;
using ADBMCPSharp.Models;
using Microsoft.Extensions.Options;

namespace ADBMCPSharp.Services;

public sealed class AdbDiscoveryService(
    DeviceInventory inventory,
    CapabilityPolicy policy,
    IAdbTransport transport,
    IOptions<AdbOptions> options)
{
    private readonly AdbOptions _options = options.Value;

    public IReadOnlyList<AdbServerSummary> ListServers() => inventory.ServerAliases
        .Select(alias =>
        {
            inventory.TryGetServer(alias, out var configured);
            return new AdbServerSummary(configured.Alias, configured.Server.Mode.ToString(), policy.Discovery);
        })
        .ToArray();

    public async Task<DiscoveryResult> DiscoverAsync(string serverAlias, CancellationToken cancellationToken)
    {
        if (!inventory.TryGetServer(serverAlias, out var configured))
            return Failure(serverAlias, OperationState.NotFound, "Unknown ADB server alias.");
        if (!policy.Discovery)
            return Failure(configured.Alias, OperationState.Denied, "Passive ADB discovery is disabled.");

        var check = await transport.ExecuteServerAsync(configured.Server, AdbServerRequest.CheckMdns, cancellationToken);
        if (!check.Success) return TransportFailure(configured.Alias, check);
        if (check.Output.Contains("disabled", StringComparison.OrdinalIgnoreCase) ||
            check.Output.Contains("not available", StringComparison.OrdinalIgnoreCase))
            return Failure(configured.Alias, OperationState.Indeterminate, "mDNS discovery is unavailable on the configured ADB server.");

        var services = await transport.ExecuteServerAsync(configured.Server, AdbServerRequest.ListMdnsServices, cancellationToken);
        if (!services.Success) return TransportFailure(configured.Alias, services);

        var candidates = AdbMdnsParser.Parse(services.Output, _options.MaxDiscoveryResults);
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(_options.DiscoveryHandleLifetimeSeconds);
        var advertisements = candidates.Select((candidate, index) => new DiscoveredAdbService(
            CreateOpaqueHandle(),
            $"advertisement-{index + 1:D2}",
            candidate.ServiceType,
            string.Equals(candidate.ServiceType, "Pairing", StringComparison.Ordinal),
            expiresAt)).ToArray();

        return new(
            configured.Alias,
            OperationState.ObservedComplete,
            true,
            advertisements.Length,
            advertisements,
            advertisements.Length == _options.MaxDiscoveryResults
                ? "The configured discovery result limit was reached; additional advertisements may exist."
                : null);
    }

    private static string CreateOpaqueHandle() => Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    private static DiscoveryResult TransportFailure(string alias, AdbExecutionResult result)
    {
        var state = result.FailureKind switch
        {
            AdbFailureKind.TimedOut => OperationState.TimedOut,
            AdbFailureKind.Offline => OperationState.Offline,
            AdbFailureKind.Unauthorized => OperationState.Unauthorized,
            AdbFailureKind.Unavailable => OperationState.Indeterminate,
            _ => OperationState.Failed,
        };
        var message = result.FailureKind switch
        {
            AdbFailureKind.TimedOut => "ADB mDNS discovery timed out.",
            AdbFailureKind.Offline => "The configured ADB server is offline.",
            AdbFailureKind.Unauthorized => "The configured ADB server rejected the discovery request.",
            AdbFailureKind.Unavailable => "ADB mDNS discovery is unavailable on the configured server.",
            AdbFailureKind.Cancelled => "ADB mDNS discovery was cancelled.",
            _ => "ADB mDNS discovery failed.",
        };
        return Failure(alias, state, message);
    }

    private static DiscoveryResult Failure(string alias, OperationState state, string message) =>
        new(alias, state, false, 0, [], message);
}
