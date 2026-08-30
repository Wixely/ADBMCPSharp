using ADBMCPSharp.Adb;
using ADBMCPSharp.Configuration;
using ADBMCPSharp.Models;
using ADBMCPSharp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ADBMCPSharp.Tests;

public sealed class AdbConnectionServiceTests
{
    [Fact]
    public async Task HealthReportsUnauthorizedWithoutExposingSelector()
    {
        var transport = new FakeTransport();
        transport.States.Enqueue(new(false, "", AdbFailureKind.Unauthorized, "Authorization is required."));
        var service = CreateService(transport, globalEnabled: false, deviceEnabled: false);

        var result = await service.GetHealthAsync("test-device", CancellationToken.None);

        Assert.Equal(OperationState.Unauthorized, result.State);
        Assert.Equal("unauthorized", result.ConnectionState);
        Assert.True(result.Reachable);
        Assert.False(result.Authorized);
        Assert.DoesNotContain("configured-selector", result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task ConnectRequiresBothGatesAndConfirmation(bool globalEnabled, bool deviceEnabled, bool confirmChange)
    {
        var transport = new FakeTransport();
        var service = CreateService(transport, globalEnabled, deviceEnabled);

        var result = await service.ConnectAsync("test-device", confirmChange, CancellationToken.None);

        Assert.Equal(OperationState.Denied, result.State);
        Assert.Empty(transport.ConnectionRequests);
    }

    [Fact]
    public async Task ConnectUsesConfiguredProfileAndRetriesVerification()
    {
        var transport = new FakeTransport();
        transport.States.Enqueue(new(false, "", AdbFailureKind.Unavailable, "Unavailable."));
        transport.States.Enqueue(new(false, "", AdbFailureKind.Offline, "Offline."));
        transport.States.Enqueue(new(true, "device"));
        var service = CreateService(transport, globalEnabled: true, deviceEnabled: true);

        var result = await service.ConnectAsync("TEST-DEVICE", confirmChange: true, CancellationToken.None);

        Assert.Equal(OperationState.ObservedComplete, result.State);
        Assert.Equal("online", result.ConnectionState);
        Assert.True(result.Verified);
        var request = Assert.Single(transport.ConnectionRequests);
        Assert.Equal(AdbConnectionRequest.Connect, request.Request);
        Assert.Equal("configured-selector", request.Selector);
        Assert.Equal(AdbServerMode.Remote, request.Server.Mode);
    }

    [Fact]
    public async Task DisconnectIsIdempotentWhenDeviceIsAlreadyAbsent()
    {
        var transport = new FakeTransport();
        transport.States.Enqueue(new(false, "", AdbFailureKind.Unavailable, "Unavailable."));
        var service = CreateService(transport, globalEnabled: true, deviceEnabled: true);

        var result = await service.DisconnectAsync("test-device", confirmChange: true, CancellationToken.None);

        Assert.Equal(OperationState.ObservedComplete, result.State);
        Assert.True(result.Verified);
        Assert.Empty(transport.ConnectionRequests);
    }

    [Fact]
    public async Task ReconnectPerformsBoundedDisconnectThenConnectAndVerifies()
    {
        var transport = new FakeTransport();
        transport.States.Enqueue(new(true, "device"));
        var service = CreateService(transport, globalEnabled: true, deviceEnabled: true);

        var result = await service.ReconnectAsync("test-device", confirmChange: true, CancellationToken.None);

        Assert.Equal(OperationState.ObservedComplete, result.State);
        Assert.Equal(
            [AdbConnectionRequest.Disconnect, AdbConnectionRequest.Connect],
            transport.ConnectionRequests.Select(x => x.Request));
    }

    private static AdbConnectionService CreateService(
        FakeTransport transport, bool globalEnabled, bool deviceEnabled)
    {
        var options = new AdbOptions
        {
            ConnectionVerificationAttempts = 2,
            ConnectionRetryDelayMilliseconds = 0,
            Servers = new(StringComparer.OrdinalIgnoreCase)
            {
                ["remote-server"] = new() { Mode = AdbServerMode.Remote, Host = "example.invalid", Port = 5040 },
            },
            Devices = new(StringComparer.OrdinalIgnoreCase)
            {
                ["test-device"] = new()
                {
                    Server = "remote-server",
                    Selector = "configured-selector",
                    Capabilities = new() { AllowConnectionManagement = deviceEnabled },
                },
            },
        };
        var inventory = new DeviceInventory(Options.Create(options));
        var policy = new CapabilityPolicy(Options.Create(new PolicyOptions
        {
            ConnectionManagementEnabled = globalEnabled,
        }));
        return new(
            inventory,
            policy,
            transport,
            new DeviceOperationCoordinator(),
            Options.Create(options),
            NullLogger<AdbConnectionService>.Instance);
    }

    private sealed class FakeTransport : IAdbTransport
    {
        public Queue<AdbExecutionResult> States { get; } = new();
        public List<ConnectionInvocation> ConnectionRequests { get; } = [];

        public Task<AdbExecutionResult> ExecuteServerAsync(
            AdbServerOptions server, AdbServerRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AdbExecutionResult> ExecuteAsync(
            AdbServerOptions server, string deviceSelector, AdbRequest request, CancellationToken cancellationToken)
        {
            Assert.Equal(AdbRequestKind.GetState, request.Kind);
            return Task.FromResult(States.Count > 0
                ? States.Dequeue()
                : new AdbExecutionResult(false, "", AdbFailureKind.Unavailable, "Unavailable."));
        }

        public Task<AdbExecutionResult> ExecuteConnectionAsync(
            AdbServerOptions server,
            string deviceSelector,
            AdbConnectionRequest request,
            CancellationToken cancellationToken)
        {
            ConnectionRequests.Add(new(server, deviceSelector, request));
            return Task.FromResult(new AdbExecutionResult(true, "accepted"));
        }
    }

    private sealed record ConnectionInvocation(
        AdbServerOptions Server, string Selector, AdbConnectionRequest Request);
}
