using System.Text.Json;
using ADBMCPSharp.Adb;
using ADBMCPSharp.Configuration;
using ADBMCPSharp.Models;
using ADBMCPSharp.Services;
using Microsoft.Extensions.Options;

namespace ADBMCPSharp.Tests;

public sealed class AdbDiscoveryServiceTests
{
    [Fact]
    public void ServerInventoryOmitsCoordinates()
    {
        var service = CreateService(new FakeTransport(_ => throw new InvalidOperationException()), discoveryEnabled: true);
        var json = JsonSerializer.Serialize(service.ListServers());

        Assert.Contains("remote", json);
        Assert.Contains("Remote", json);
        Assert.DoesNotContain("example.invalid", json);
        Assert.DoesNotContain("5037", json);
    }

    [Fact]
    public async Task DiscoveryIsDeniedByDefaultWithoutCallingAdb()
    {
        var fake = new FakeTransport(_ => throw new InvalidOperationException("Transport should not be called."));
        var result = await CreateService(fake).DiscoverAsync("local", CancellationToken.None);

        Assert.Equal(OperationState.Denied, result.State);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task EnabledDiscoveryReturnsBoundedRedactedAdvertisements()
    {
        var fake = new FakeTransport(request => request switch
        {
            AdbServerRequest.CheckMdns => new(true, "mdns daemon version 1"),
            AdbServerRequest.ListMdnsServices => new(true, """
                private-device-name _adb-tls-connect._tcp 192.0.2.10:37123
                second-private-name _adb-tls-pairing._tcp 192.0.2.11:38888
                third-private-name _adb._tcp 192.0.2.12:5555
                """),
            _ => throw new InvalidOperationException(),
        });

        var result = await CreateService(fake, discoveryEnabled: true, maximumResults: 2)
            .DiscoverAsync("remote", CancellationToken.None);
        var json = JsonSerializer.Serialize(result);

        Assert.Equal(OperationState.ObservedComplete, result.State);
        Assert.True(result.MdnsAvailable);
        Assert.Equal(2, result.AdvertisementCount);
        Assert.Equal("WirelessDebugging", result.Advertisements[0].ServiceType);
        Assert.Equal("Pairing", result.Advertisements[1].ServiceType);
        Assert.True(result.Advertisements[1].PairingWindowOpen);
        Assert.All(result.Advertisements, advertisement => Assert.Matches("^[0-9a-f]{24}$", advertisement.DiscoveryHandle));
        Assert.Contains("limit", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-device", json);
        Assert.DoesNotContain("192.0.2", json);
        Assert.DoesNotContain("37123", json);
        Assert.Equal(AdbServerMode.Remote, fake.LastServerMode);
    }

    [Fact]
    public async Task UnavailableMdnsReturnsIndeterminateWithoutListing()
    {
        var fake = new FakeTransport(_ => new(true, "mDNS discovery disabled"));
        var result = await CreateService(fake, discoveryEnabled: true).DiscoverAsync("local", CancellationToken.None);

        Assert.Equal(OperationState.Indeterminate, result.State);
        Assert.False(result.MdnsAvailable);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task EverySupportedAdvertisementIsRedactedAndGetsUniqueExpiringHandle()
    {
        const string privateOutput = """
            sensitive-legacy _adb._tcp 192.0.2.10:5555
            sensitive-connect _adb-tls-connect._tcp 192.0.2.11:37123
            sensitive-pair _adb-tls-pairing._tcp [2001:db8::12]:38888
            """;
        var fake = new FakeTransport(request => request switch
        {
            AdbServerRequest.CheckMdns => new(true, "mdns daemon version 1"),
            AdbServerRequest.ListMdnsServices => new(true, privateOutput),
            _ => throw new InvalidOperationException(),
        });
        var before = DateTimeOffset.UtcNow.AddSeconds(29);

        var result = await CreateService(fake, discoveryEnabled: true, handleLifetimeSeconds: 30)
            .DiscoverAsync("local", CancellationToken.None);
        var after = DateTimeOffset.UtcNow.AddSeconds(31);
        var json = JsonSerializer.Serialize(result);

        Assert.Equal(["LegacyTcpAdb", "WirelessDebugging", "Pairing"],
            result.Advertisements.Select(advertisement => advertisement.ServiceType));
        Assert.False(result.Advertisements[0].PairingWindowOpen);
        Assert.False(result.Advertisements[1].PairingWindowOpen);
        Assert.True(result.Advertisements[2].PairingWindowOpen);
        Assert.Equal(3, result.Advertisements.Select(advertisement => advertisement.DiscoveryHandle).Distinct().Count());
        Assert.All(result.Advertisements, advertisement =>
        {
            Assert.Matches("^[0-9a-f]{24}$", advertisement.DiscoveryHandle);
            Assert.InRange(advertisement.ExpiresAtUtc, before, after);
        });
        foreach (var sensitiveFragment in new[]
        {
            "sensitive-legacy", "sensitive-connect", "sensitive-pair",
            "192.0.2", "2001:db8", "5555", "37123", "38888",
        }) Assert.DoesNotContain(sensitiveFragment, json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(AdbFailureKind.TimedOut, OperationState.TimedOut)]
    [InlineData(AdbFailureKind.Offline, OperationState.Offline)]
    [InlineData(AdbFailureKind.Unauthorized, OperationState.Unauthorized)]
    [InlineData(AdbFailureKind.Unavailable, OperationState.Indeterminate)]
    [InlineData(AdbFailureKind.Cancelled, OperationState.Failed)]
    [InlineData(AdbFailureKind.Failed, OperationState.Failed)]
    public async Task TransportFailuresAreClassifiedWithoutLeakingTransportMessage(
        AdbFailureKind failureKind, OperationState expectedState)
    {
        var fake = new FakeTransport(_ => new(
            false, "", failureKind, "private-name at 192.0.2.44:37123"));

        var result = await CreateService(fake, discoveryEnabled: true)
            .DiscoverAsync("local", CancellationToken.None);
        var json = JsonSerializer.Serialize(result);

        Assert.Equal(expectedState, result.State);
        Assert.False(result.MdnsAvailable);
        Assert.DoesNotContain("private-name", json);
        Assert.DoesNotContain("192.0.2.44", json);
        Assert.DoesNotContain("37123", json);
    }

    [Fact]
    public async Task UnknownServerAliasDoesNotCallTransport()
    {
        var fake = new FakeTransport(_ => throw new InvalidOperationException("Transport should not be called."));

        var result = await CreateService(fake, discoveryEnabled: true)
            .DiscoverAsync("unknown", CancellationToken.None);

        Assert.Equal(OperationState.NotFound, result.State);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task EmptyAdvertisementListIsACompletedAvailableResult()
    {
        var fake = new FakeTransport(request => request switch
        {
            AdbServerRequest.CheckMdns => new(true, "mdns daemon version 1"),
            AdbServerRequest.ListMdnsServices => new(true, "List of discovered mdns services"),
            _ => throw new InvalidOperationException(),
        });

        var result = await CreateService(fake, discoveryEnabled: true)
            .DiscoverAsync("local", CancellationToken.None);

        Assert.Equal(OperationState.ObservedComplete, result.State);
        Assert.True(result.MdnsAvailable);
        Assert.Empty(result.Advertisements);
        Assert.Null(result.Message);
    }

    [Fact]
    public async Task DiscoveryHandlesRotateBetweenScans()
    {
        var fake = new FakeTransport(request => request switch
        {
            AdbServerRequest.CheckMdns => new(true, "mdns daemon version 1"),
            AdbServerRequest.ListMdnsServices => new(true, "private _adb._tcp 192.0.2.10:5555"),
            _ => throw new InvalidOperationException(),
        });
        var service = CreateService(fake, discoveryEnabled: true);

        var first = await service.DiscoverAsync("local", CancellationToken.None);
        var second = await service.DiscoverAsync("local", CancellationToken.None);

        Assert.NotEqual(first.Advertisements[0].DiscoveryHandle, second.Advertisements[0].DiscoveryHandle);
    }

    private static AdbDiscoveryService CreateService(
        FakeTransport fake,
        bool discoveryEnabled = false,
        int maximumResults = 25,
        int handleLifetimeSeconds = 60)
    {
        var adb = new AdbOptions
        {
            MaxDiscoveryResults = maximumResults,
            DiscoveryHandleLifetimeSeconds = handleLifetimeSeconds,
            Servers = new()
            {
                ["local"] = new(),
                ["remote"] = new() { Mode = AdbServerMode.Remote, Host = "example.invalid" },
            },
        };
        var options = Options.Create(adb);
        var inventory = new DeviceInventory(options);
        var policy = new CapabilityPolicy(Options.Create(new PolicyOptions { DiscoveryEnabled = discoveryEnabled }));
        return new(inventory, policy, fake, options);
    }

    private sealed class FakeTransport(Func<AdbServerRequest, AdbExecutionResult> handler) : IAdbTransport
    {
        public List<AdbServerRequest> Requests { get; } = [];
        public AdbServerMode? LastServerMode { get; private set; }

        public Task<AdbExecutionResult> ExecuteServerAsync(AdbServerOptions server, AdbServerRequest request, CancellationToken cancellationToken)
        {
            LastServerMode = server.Mode;
            Requests.Add(request);
            return Task.FromResult(handler(request));
        }

        public Task<AdbExecutionResult> ExecuteAsync(AdbServerOptions server, string deviceSelector, AdbRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Device transport should not be called.");
    }
}
