using ADBMCPSharp.Adb;
using ADBMCPSharp.Configuration;
using ADBMCPSharp.Models;
using ADBMCPSharp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ADBMCPSharp.Tests;

public sealed class AndroidDeviceServiceTests
{
    [Fact]
    public async Task StatusReturnsBoundedParsedFactsWithoutSelector()
    {
        var fake = new FakeTransport(request => request.Kind switch
        {
            AdbRequestKind.GetState => Ok("device"),
            AdbRequestKind.GetManufacturer => Ok("Example"),
            AdbRequestKind.GetModel => Ok("Tablet"),
            AdbRequestKind.GetAndroidVersion => Ok("16"),
            AdbRequestKind.GetApiLevel => Ok("36"),
            AdbRequestKind.GetPowerState => Ok("mWakefulness=Awake\nDisplay Power: state=ON"),
            AdbRequestKind.GetForegroundWindow => Ok("mCurrentFocus=Window{x u0 org.example.player/.Main}"),
            _ => throw new InvalidOperationException(),
        });
        var service = CreateService(fake);

        var result = await service.GetStatusAsync("living-room", CancellationToken.None);

        Assert.Equal(OperationState.ObservedComplete, result.State);
        Assert.Equal("online", result.ConnectionState);
        Assert.Equal(36, result.ApiLevel);
        Assert.Equal("org.example.player", result.ForegroundPackage);
        Assert.DoesNotContain("sensitive-selector", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task PowerControlIsDeniedByDefaultWithoutCallingAdb()
    {
        var fake = new FakeTransport(_ => throw new InvalidOperationException("Transport should not be called."));
        var result = await CreateService(fake).WakeAsync("living-room", CancellationToken.None);
        Assert.Equal(OperationState.Denied, result.State);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task EnabledWakeIsVerified()
    {
        var fake = new FakeTransport(request => request.Kind switch
        {
            AdbRequestKind.Wake => Ok(),
            AdbRequestKind.GetPowerState => Ok("mWakefulness=Awake"),
            _ => throw new InvalidOperationException(),
        });
        var policy = new PolicyOptions { PowerControlEnabled = true };
        var result = await CreateService(fake, policy).WakeAsync("living-room", CancellationToken.None);
        Assert.Equal(OperationState.ObservedComplete, result.State);
        Assert.True(result.Verified);
    }

    [Fact]
    public async Task NavigationRequiresBothCategoryAndActionAllowlist()
    {
        var fake = new FakeTransport(_ => Ok());
        var policy = new PolicyOptions { NavigationControlEnabled = true, AllowedNavigationActions = [NavigationAction.Home] };
        var service = CreateService(fake, policy);

        var denied = await service.NavigateAsync("living-room", NavigationAction.Back, CancellationToken.None);
        var accepted = await service.NavigateAsync("living-room", NavigationAction.Home, CancellationToken.None);

        Assert.Equal(OperationState.Denied, denied.State);
        Assert.Equal(OperationState.Accepted, accepted.State);
        Assert.Single(fake.Requests);
        Assert.Equal(AdbRequestKind.Navigation, fake.Requests[0].Kind);
        Assert.Equal(nameof(NavigationAction.Home), fake.Requests[0].Value);
    }

    [Fact]
    public async Task LaunchUsesConfiguredPackageNotClientInput()
    {
        var fake = new FakeTransport(request => request.Kind switch
        {
            AdbRequestKind.LaunchPackage => Ok(),
            AdbRequestKind.GetForegroundWindow => Ok("mCurrentFocus=Window{x u0 org.example.player/.Main}"),
            AdbRequestKind.GetDreamState => Ok("mCurrentDream=null"),
            AdbRequestKind.StopDreaming => Ok(),
            _ => throw new InvalidOperationException(),
        });
        var policy = new PolicyOptions { AppLaunchEnabled = true };
        var result = await CreateService(fake, policy).LaunchAppAsync("living-room", "player", CancellationToken.None);

        Assert.Equal(OperationState.ObservedComplete, result.State);
        var launch = Assert.Single(fake.Requests, request => request.Kind == AdbRequestKind.LaunchPackage);
        Assert.Equal("org.example.player", launch.Value);
    }

    [Fact]
    public async Task LaunchFallsBackToLeanbackCategoryWhenStandardLaunchIsNotForeground()
    {
        var foregroundChecks = 0;
        var fake = new FakeTransport(request => request.Kind switch
        {
            AdbRequestKind.LaunchPackage => Ok(),
            AdbRequestKind.GetForegroundWindow => ++foregroundChecks == 1
                ? Ok("mCurrentFocus=Window{x u0 org.example.launcher/.Home}")
                : Ok("mCurrentFocus=Window{x u0 org.example.player/.Main}"),
            AdbRequestKind.GetDreamState => Ok("mCurrentDream=null"),
            AdbRequestKind.StopDreaming => Ok(),
            _ => throw new InvalidOperationException(),
        });
        var policy = new PolicyOptions { AppLaunchEnabled = true };

        var result = await CreateService(fake, policy).LaunchAppAsync("living-room", "player", CancellationToken.None);

        Assert.Equal(OperationState.ObservedComplete, result.State);
        Assert.True(result.Verified);
        var launches = fake.Requests.Where(request => request.Kind == AdbRequestKind.LaunchPackage).ToArray();
        Assert.Equal(2, launches.Length);
        Assert.False(launches[0].Flag);
        Assert.True(launches[1].Flag);
    }

    [Fact]
    public async Task WakeAndForegroundRequiresPowerGateAndDismissesDreamBeforeLaunch()
    {
        var fake = new FakeTransport(request => request.Kind switch
        {
            AdbRequestKind.Wake or AdbRequestKind.StopDreaming or AdbRequestKind.LaunchPackage => Ok(),
            AdbRequestKind.GetForegroundWindow => Ok("mCurrentFocus=Window{x u0 org.example.player/.Main}"),
            AdbRequestKind.GetDreamState => Ok("mCurrentDream=null"),
            _ => throw new InvalidOperationException(),
        });
        var deniedPolicy = new PolicyOptions { AppLaunchEnabled = true };
        var enabledPolicy = new PolicyOptions { AppLaunchEnabled = true, PowerControlEnabled = true };

        var denied = await CreateService(fake, deniedPolicy).LaunchAppAsync(
            "living-room", "player", AppLaunchMode.WakeAndForeground, CancellationToken.None);
        var result = await CreateService(fake, enabledPolicy).LaunchAppAsync(
            "living-room", "player", AppLaunchMode.WakeAndForeground, CancellationToken.None);

        Assert.Equal(OperationState.Denied, denied.State);
        Assert.Equal(OperationState.ObservedComplete, result.State);
        Assert.Equal(
            [AdbRequestKind.Wake, AdbRequestKind.StopDreaming, AdbRequestKind.LaunchPackage],
            fake.Requests.Take(3).Select(request => request.Kind));
    }

    [Fact]
    public async Task InstalledAppListingIsDeniedByDefault()
    {
        var fake = new FakeTransport(_ => throw new InvalidOperationException("Transport should not be called."));

        var result = await CreateService(fake).ListInstalledAppsAsync("living-room", InstalledAppScope.User, CancellationToken.None);

        Assert.Equal(OperationState.Denied, result.State);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task InstalledAppListingIsBoundedWhenEnabled()
    {
        var fake = new FakeTransport(request => request.Kind == AdbRequestKind.ListInstalledPackages
            ? Ok("package:org.example.one\npackage:org.example.two\npackage:org.example.three")
            : throw new InvalidOperationException());
        var policy = new PolicyOptions { InstalledAppListingEnabled = true };

        var result = await CreateService(fake, policy, maxInstalledAppResults: 2)
            .ListInstalledAppsAsync("living-room", InstalledAppScope.User, CancellationToken.None);

        Assert.Equal(OperationState.ObservedComplete, result.State);
        Assert.Equal(2, result.Count);
        Assert.True(result.Truncated);
        Assert.Equal(["org.example.one", "org.example.two"], result.Apps.Select(app => app.PackageName));
        Assert.Equal(nameof(InstalledAppScope.User), fake.Requests[0].Value);
    }

    [Fact]
    public async Task MediaInspectionRedactsUnrecognizedPackage()
    {
        var fake = new FakeTransport(request => request.Kind == AdbRequestKind.GetMediaSession
            ? Ok("Media button session is MediaSessionRecord{x u0 org.private.player/session}\nactive=true")
            : throw new InvalidOperationException());
        var policy = new PolicyOptions { MediaInspectionEnabled = true };

        var result = await CreateService(fake, policy).GetMediaStatusAsync("living-room", CancellationToken.None);

        Assert.Equal(OperationState.ObservedComplete, result.State);
        Assert.Empty(result.Sessions);
        Assert.DoesNotContain("org.private.player", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task MediaActionRequiresCategoryAndActionAllowlist()
    {
        var fake = new FakeTransport(_ => Ok());
        var policy = new PolicyOptions { MediaControlEnabled = true, AllowedMediaActions = [MediaAction.Play, MediaAction.Pause] };
        var service = CreateService(fake, policy);

        var denied = await service.SendMediaActionAsync("living-room", MediaAction.Stop, CancellationToken.None);
        var accepted = await service.SendMediaActionAsync("living-room", MediaAction.Play, CancellationToken.None);

        Assert.Equal(OperationState.Denied, denied.State);
        Assert.Equal(OperationState.Accepted, accepted.State);
        Assert.Single(fake.Requests);
        Assert.Equal(nameof(MediaAction.Play), fake.Requests[0].Value);
    }

    private static AndroidDeviceService CreateService(
        FakeTransport fake,
        PolicyOptions? policy = null,
        int maxInstalledAppResults = 200)
    {
        var adb = new AdbOptions
        {
            VerificationDelayMilliseconds = 0,
            AppLaunchVerificationAttempts = 1,
            MaxInstalledAppResults = maxInstalledAppResults,
            Devices = new()
            {
                ["living-room"] = new()
                {
                    Server = "local",
                    Selector = "sensitive-selector",
                    AllowedApps = new() { ["player"] = new() { Package = "org.example.player" } },
                },
            },
        };
        var adbOptions = Options.Create(adb);
        var inventory = new DeviceInventory(adbOptions);
        var capabilityPolicy = new CapabilityPolicy(Options.Create(policy ?? new()));
        return new(inventory, capabilityPolicy, fake, new DeviceOperationCoordinator(), adbOptions, NullLogger<AndroidDeviceService>.Instance);
    }

    private static AdbExecutionResult Ok(string output = "") => new(true, output);

    private sealed class FakeTransport(Func<AdbRequest, AdbExecutionResult> handler) : IAdbTransport
    {
        public List<AdbRequest> Requests { get; } = [];

        public Task<AdbExecutionResult> ExecuteServerAsync(AdbServerOptions server, AdbServerRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Server-level transport should not be called.");

        public Task<AdbExecutionResult> ExecuteAsync(AdbServerOptions server, string deviceSelector, AdbRequest request, CancellationToken cancellationToken)
        {
            Assert.Equal("sensitive-selector", deviceSelector);
            Requests.Add(request);
            return Task.FromResult(handler(request));
        }
    }
}
