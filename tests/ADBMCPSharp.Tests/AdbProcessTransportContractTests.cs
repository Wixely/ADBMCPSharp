using ADBMCPSharp.Adb;
using ADBMCPSharp.Configuration;

namespace ADBMCPSharp.Tests;

public sealed class AdbProcessTransportContractTests
{
    [Fact]
    public void ForegroundInspectionUsesWindowSummaryContainingFocusFields()
    {
        var arguments = AdbProcessTransport.BuildDeviceArguments(
            new(), "configured-selector", new(AdbRequestKind.GetForegroundWindow));

        Assert.Equal(["-s", "configured-selector", "shell", "dumpsys", "window"], arguments);
    }

    [Fact]
    public void LocalMdnsDiscoveryUsesOnlyFixedServerCommand()
    {
        var arguments = AdbProcessTransport.BuildServerArguments(new(), AdbServerRequest.ListMdnsServices);

        Assert.Equal(["mdns", "services"], arguments);
        Assert.DoesNotContain("-s", arguments);
    }

    [Fact]
    public void RemoteMdnsDiscoveryRunsThroughConfiguredServer()
    {
        var server = new AdbServerOptions { Mode = AdbServerMode.Remote, Host = "example.invalid", Port = 5040 };

        var arguments = AdbProcessTransport.BuildServerArguments(server, AdbServerRequest.CheckMdns);

        Assert.Equal(["-H", "example.invalid", "-P", "5040", "mdns", "check"], arguments);
        Assert.DoesNotContain("-s", arguments);
    }

    [Fact]
    public void ConnectionLifecycleUsesConfiguredSelectorWithoutDeviceScopeFlag()
    {
        var connect = AdbProcessTransport.BuildConnectionArguments(
            new(), "configured-selector", AdbConnectionRequest.Connect);
        var disconnect = AdbProcessTransport.BuildConnectionArguments(
            new() { Mode = AdbServerMode.Remote, Host = "example.invalid", Port = 5040 },
            "configured-selector",
            AdbConnectionRequest.Disconnect);

        Assert.Equal(["connect", "configured-selector"], connect);
        Assert.Equal(["-H", "example.invalid", "-P", "5040", "disconnect", "configured-selector"], disconnect);
        Assert.DoesNotContain("-s", connect);
        Assert.DoesNotContain("-s", disconnect);
    }

    [Theory]
    [InlineData(InstalledAppScope.All, null)]
    [InlineData(InstalledAppScope.User, "-3")]
    [InlineData(InstalledAppScope.System, "-s")]
    public void InstalledAppListingUsesOnlyClosedScopeArguments(InstalledAppScope scope, string? expectedScopeArgument)
    {
        var arguments = AdbProcessTransport.BuildDeviceArguments(
            new(), "configured-selector", new(AdbRequestKind.ListInstalledPackages, scope.ToString()));

        Assert.Equal(["-s", "configured-selector", "shell", "pm", "list", "packages"], arguments.Take(6));
        Assert.Equal(expectedScopeArgument, arguments.Count == 7 ? arguments[6] : null);
    }

    [Theory]
    [InlineData(MediaAction.Play, "KEYCODE_MEDIA_PLAY")]
    [InlineData(MediaAction.Pause, "KEYCODE_MEDIA_PAUSE")]
    [InlineData(MediaAction.Stop, "KEYCODE_MEDIA_STOP")]
    [InlineData(MediaAction.FastForward, "KEYCODE_MEDIA_FAST_FORWARD")]
    public void MediaActionsMapToFixedKeyCodes(MediaAction action, string expectedKeyCode)
    {
        var arguments = AdbProcessTransport.BuildDeviceArguments(
            new(), "configured-selector", new(AdbRequestKind.MediaAction, action.ToString()));

        Assert.Equal(expectedKeyCode, arguments[^1]);
    }

    [Theory]
    [InlineData(VolumeAction.Up, "KEYCODE_VOLUME_UP")]
    [InlineData(VolumeAction.Down, "KEYCODE_VOLUME_DOWN")]
    [InlineData(VolumeAction.Mute, "KEYCODE_VOLUME_MUTE")]
    public void VolumeActionsMapToFixedKeyCodes(VolumeAction action, string expectedKeyCode)
    {
        var arguments = AdbProcessTransport.BuildDeviceArguments(new(), "configured-selector", AdbRequest.Volume(action));
        Assert.Equal(expectedKeyCode, arguments[^1]);
    }

    [Fact]
    public void PackageChangesUseFixedAdbVerbsAndConfiguredValues()
    {
        var install = AdbProcessTransport.BuildDeviceArguments(
            new(), "configured-selector", new(AdbRequestKind.InstallApk, @"C:\configured\release.apk", true));
        var uninstall = AdbProcessTransport.BuildDeviceArguments(
            new(), "configured-selector", new(AdbRequestKind.UninstallPackage, "org.example.player"));

        Assert.Equal(["-s", "configured-selector", "install", "-r", @"C:\configured\release.apk"], install);
        Assert.Equal(["-s", "configured-selector", "uninstall", "org.example.player"], uninstall);
    }

    [Fact]
    public void ArbitraryArgumentsRemainAfterTheConfiguredDeviceSelector()
    {
        var arguments = AdbProcessTransport.BuildDeviceArguments(
            new(),
            "configured-selector",
            new(AdbRequestKind.ArbitraryDeviceCommand, Arguments: ["shell", "getprop", "ro.build.type"]));

        Assert.Equal(
            ["-s", "configured-selector", "shell", "getprop", "ro.build.type"],
            arguments);
    }

    [Theory]
    [InlineData(AdbRequestKind.GetBatteryDiagnostic, "dumpsys", "battery")]
    [InlineData(AdbRequestKind.GetMemoryDiagnostic, "cat", "/proc/meminfo")]
    [InlineData(AdbRequestKind.GetStorageDiagnostic, "df", "-k")]
    [InlineData(AdbRequestKind.GetCpuLoadDiagnostic, "cat", "/proc/loadavg")]
    [InlineData(AdbRequestKind.GetRuntimeDiagnostic, "cat", "/proc/uptime")]
    [InlineData(AdbRequestKind.GetDisplaySizeDiagnostic, "wm", "size")]
    [InlineData(AdbRequestKind.GetSelinuxDiagnostic, "getenforce", null)]
    public void DiagnosticsUseOnlyFixedReadOnlyArguments(AdbRequestKind kind, string verb, string? value)
    {
        var arguments = AdbProcessTransport.BuildDeviceArguments(new(), "configured-selector", new(kind));

        Assert.Equal(["-s", "configured-selector", "shell"], arguments.Take(3));
        Assert.Contains(verb, arguments);
        if (value is not null) Assert.Contains(value, arguments);
    }
}
