using ADBMCPSharp.Adb;
using ADBMCPSharp.Configuration;
using ADBMCPSharp.Models;
using ADBMCPSharp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ADBMCPSharp.Tests;

public sealed class ArbitraryAdbServiceTests
{
    [Fact]
    public async Task RequiresGlobalDeviceAndConfirmationGates()
    {
        var (disabled, disabledTransport) = CreateService(globalEnabled: false, deviceEnabled: true);
        var (unconfirmed, unconfirmedTransport) = CreateService(globalEnabled: true, deviceEnabled: true);

        var disabledResult = await disabled.ExecuteAsync("test-device", ["shell", "id"], true, CancellationToken.None);
        var unconfirmedResult = await unconfirmed.ExecuteAsync("test-device", ["shell", "id"], false, CancellationToken.None);

        Assert.Equal(OperationState.Denied, disabledResult.State);
        Assert.Equal(OperationState.Denied, unconfirmedResult.State);
        Assert.Empty(disabledTransport.Requests);
        Assert.Empty(unconfirmedTransport.Requests);
    }

    [Theory]
    [MemberData(nameof(RejectedArguments))]
    public async Task RejectsArgumentsThatCouldEscapeDeviceScope(IReadOnlyList<string> arguments)
    {
        var (service, transport) = CreateService(globalEnabled: true, deviceEnabled: true);

        var result = await service.ExecuteAsync("test-device", arguments, true, CancellationToken.None);

        Assert.Equal(OperationState.Denied, result.State);
        Assert.Empty(transport.Requests);
    }

    public static TheoryData<IReadOnlyList<string>> RejectedArguments => new()
    {
        Array.Empty<string>(),
        new[] { "-s", "another-device", "shell", "id" },
        new[] { "connect", "example.invalid:5555" },
        new[] { "kill-server" },
        new[] { "shell", "line\nbreak" },
    };

    [Fact]
    public async Task ExecutesArgumentsOnlyForConfiguredDeviceAndReturnsBoundedTransportOutput()
    {
        var (service, transport) = CreateService(globalEnabled: true, deviceEnabled: true);
        transport.Result = new(true, "uid=2000(shell)");

        var result = await service.ExecuteAsync("test-device", ["shell", "id"], true, CancellationToken.None);

        Assert.Equal(OperationState.ObservedComplete, result.State);
        Assert.Equal("uid=2000(shell)", result.Output);
        var request = Assert.Single(transport.Requests);
        Assert.Equal(AdbRequestKind.ArbitraryDeviceCommand, request.Kind);
        Assert.Equal(["shell", "id"], request.Arguments);
        Assert.Equal(30, request.TimeoutSeconds);
    }

    private static (ArbitraryAdbService Service, FakeTransport Transport) CreateService(bool globalEnabled, bool deviceEnabled)
    {
        var adb = new AdbOptions
        {
            Devices = new()
            {
                ["test-device"] = new()
                {
                    Selector = "configured-selector",
                    Capabilities = new() { AllowArbitraryCommands = deviceEnabled },
                },
            },
        };
        var adbOptions = Options.Create(adb);
        var transport = new FakeTransport();
        return (new(
            new DeviceInventory(adbOptions),
            new CapabilityPolicy(Options.Create(new PolicyOptions { ArbitraryCommandsEnabled = globalEnabled })),
            transport,
            new DeviceOperationCoordinator(),
            adbOptions,
            NullLogger<ArbitraryAdbService>.Instance), transport);
    }

    private sealed class FakeTransport : IAdbTransport
    {
        public AdbExecutionResult Result { get; set; } = new(true, string.Empty);
        public List<AdbRequest> Requests { get; } = [];

        public Task<AdbExecutionResult> ExecuteServerAsync(AdbServerOptions server, AdbServerRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<AdbExecutionResult> ExecuteAsync(AdbServerOptions server, string deviceSelector, AdbRequest request, CancellationToken cancellationToken)
        {
            Assert.Equal("configured-selector", deviceSelector);
            Requests.Add(request);
            return Task.FromResult(Result);
        }
    }
}
