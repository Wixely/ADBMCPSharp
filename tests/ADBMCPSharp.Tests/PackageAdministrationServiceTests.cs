using System.Security.Cryptography;
using ADBMCPSharp.Adb;
using ADBMCPSharp.Configuration;
using ADBMCPSharp.Models;
using ADBMCPSharp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ADBMCPSharp.Tests;

public sealed class PackageAdministrationServiceTests
{
    [Fact]
    public async Task InstallRequiresExplicitConfirmationBeforeArtifactAccess()
    {
        var (service, transport) = CreateService();

        var result = await service.InstallAsync("living-room", "player-apk", false, CancellationToken.None);

        Assert.Equal(OperationState.Denied, result.State);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task InstallVerifiesChecksumAndObservedPackage()
    {
        var apk = Path.Combine(Path.GetTempPath(), $"adbmcp-test-{Guid.NewGuid():N}.apk");
        var content = "test apk bytes"u8.ToArray();
        await File.WriteAllBytesAsync(apk, content, TestContext.Current.CancellationToken);
        try
        {
            var (service, transport) = CreateService(apk, Convert.ToHexString(SHA256.HashData(content)));
            transport.Results.Enqueue(new(true, "Success"));
            transport.Results.Enqueue(new(true, "package:/data/app/player.apk"));

            var result = await service.InstallAsync("living-room", "player-apk", true, CancellationToken.None);

            Assert.Equal(OperationState.ObservedComplete, result.State);
            Assert.True(result.Verified);
            Assert.Equal([AdbRequestKind.InstallApk, AdbRequestKind.GetPackagePath], transport.Requests.Select(request => request.Kind));
        }
        finally
        {
            File.Delete(apk);
        }
    }

    [Fact]
    public async Task UninstallUsesOnlyConfiguredPackageAndVerifiesAbsence()
    {
        var (service, transport) = CreateService();
        transport.Results.Enqueue(new(true, "package:/data/app/player.apk"));
        transport.Results.Enqueue(new(true, "Success"));
        transport.Results.Enqueue(new(false, "", AdbFailureKind.Failed, "not installed"));

        var result = await service.UninstallAsync("living-room", "player", true, CancellationToken.None);

        Assert.Equal(OperationState.ObservedComplete, result.State);
        Assert.True(result.Verified);
        Assert.Equal(
            [AdbRequestKind.GetPackagePath, AdbRequestKind.UninstallPackage, AdbRequestKind.GetPackagePath],
            transport.Requests.Select(request => request.Kind));
        Assert.All(transport.Requests, request => Assert.Equal("org.example.player", request.Value));
        Assert.Equal(300, transport.Requests[1].TimeoutSeconds);
    }

    [Fact]
    public void ArtifactListingExposesOnlyAliasesAndEffectivePermission()
    {
        var (service, _) = CreateService();

        var result = Assert.Single(service.ListInstallableApks("living-room")!);

        Assert.Equal("player-apk", result.Alias);
        Assert.Equal("Player release", result.DisplayName);
        Assert.True(result.AllowedForDevice);
    }

    private static (PackageAdministrationService Service, FakeTransport Transport) CreateService(
        string source = @"C:\configured\player.apk", string? sha256 = null)
    {
        var adb = new AdbOptions
        {
            Devices = new()
            {
                ["living-room"] = new()
                {
                    Selector = "sensitive-selector",
                    Capabilities = new() { AllowPackageInstall = true, AllowPackageUninstall = true },
                    AllowedApps = new()
                    {
                        ["player"] = new() { Package = "org.example.player", AllowUninstall = true },
                    },
                },
            },
            ApkArtifacts = new()
            {
                ["player-apk"] = new()
                {
                    Package = "org.example.player",
                    Source = source,
                    Sha256 = sha256 ?? new string('a', 64),
                    DisplayName = "Player release",
                    AllowedDevices = ["living-room"],
                },
            },
        };
        var adbOptions = Options.Create(adb);
        var transport = new FakeTransport();
        var service = new PackageAdministrationService(
            new DeviceInventory(adbOptions),
            new CapabilityPolicy(Options.Create(new PolicyOptions
            {
                PackageInstallEnabled = true,
                PackageUninstallEnabled = true,
            })),
            transport,
            new DeviceOperationCoordinator(),
            new UnusedHttpClientFactory(),
            adbOptions,
            NullLogger<PackageAdministrationService>.Instance);
        return (service, transport);
    }

    private sealed class FakeTransport : IAdbTransport
    {
        public Queue<AdbExecutionResult> Results { get; } = new();
        public List<AdbRequest> Requests { get; } = [];

        public Task<AdbExecutionResult> ExecuteServerAsync(AdbServerOptions server, AdbServerRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<AdbExecutionResult> ExecuteAsync(AdbServerOptions server, string deviceSelector, AdbRequest request, CancellationToken cancellationToken)
        {
            Assert.Equal("sensitive-selector", deviceSelector);
            Requests.Add(request);
            return Task.FromResult(Results.Dequeue());
        }
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("HTTP should not be used by these tests.");
    }
}
