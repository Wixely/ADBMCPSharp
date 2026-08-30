using ADBMCPSharp.Adb;
using ADBMCPSharp.Configuration;
using ADBMCPSharp.Models;
using ADBMCPSharp.Services;
using Microsoft.Extensions.Options;

namespace ADBMCPSharp.Tests;

public sealed class DeviceDiagnosticServiceTests
{
    [Fact]
    public async Task DiagnosticRequiresCategoryAndOptionAllowlist()
    {
        var (service, transport) = CreateService([DiagnosticKind.Battery]);

        var denied = await service.RunAsync("test-device", DiagnosticKind.Memory, CancellationToken.None);
        var allowed = await service.RunAsync("test-device", DiagnosticKind.Battery, CancellationToken.None);

        Assert.Equal(OperationState.Denied, denied.State);
        Assert.Equal(OperationState.ObservedComplete, allowed.State);
        Assert.IsType<BatteryDiagnostic>(allowed.Data);
        Assert.Equal(AdbRequestKind.GetBatteryDiagnostic, Assert.Single(transport.Requests).Kind);
    }

    [Fact]
    public void ListReportsEveryCuratedOptionAndEffectiveEnablement()
    {
        var (service, _) = CreateService([DiagnosticKind.Battery, DiagnosticKind.Security]);

        var options = service.List("test-device")!;

        Assert.Equal(Enum.GetValues<DiagnosticKind>().Length, options.Count);
        Assert.True(options.Single(option => option.Name == nameof(DiagnosticKind.Battery)).Enabled);
        Assert.False(options.Single(option => option.Name == nameof(DiagnosticKind.Storage)).Enabled);
    }

    private static (DeviceDiagnosticService Service, FakeTransport Transport) CreateService(HashSet<DiagnosticKind> allowed)
    {
        var adb = new AdbOptions
        {
            Devices = new()
            {
                ["test-device"] = new() { Selector = "configured-selector" },
            },
        };
        var transport = new FakeTransport();
        var options = Options.Create(adb);
        return (new(
            new DeviceInventory(options),
            new CapabilityPolicy(Options.Create(new PolicyOptions
            {
                DiagnosticsEnabled = true,
                AllowedDiagnostics = allowed,
            })),
            transport,
            new DeviceOperationCoordinator()), transport);
    }

    private sealed class FakeTransport : IAdbTransport
    {
        public List<AdbRequest> Requests { get; } = [];

        public Task<AdbExecutionResult> ExecuteServerAsync(AdbServerOptions server, AdbServerRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<AdbExecutionResult> ExecuteAsync(AdbServerOptions server, string deviceSelector, AdbRequest request, CancellationToken cancellationToken)
        {
            Assert.Equal("configured-selector", deviceSelector);
            Requests.Add(request);
            return Task.FromResult(request.Kind switch
            {
                AdbRequestKind.GetBatteryDiagnostic => new AdbExecutionResult(true, "level: 50\nscale: 100\nstatus: 3\nhealth: 2"),
                _ => throw new InvalidOperationException(),
            });
        }
    }
}
