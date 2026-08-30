using ADBMCPSharp.Configuration;
using ADBMCPSharp.Models;
using Microsoft.Extensions.Options;

namespace ADBMCPSharp.Services;

public sealed class CapabilityPolicy(IOptions<PolicyOptions> options)
{
    private readonly PolicyOptions _policy = options.Value;

    public bool Discovery => _policy.DiscoveryEnabled;
    public bool Inspection(ConfiguredDevice device) => _policy.InspectionEnabled && device.Device.Capabilities.Enabled;
    public bool InstalledApps(ConfiguredDevice device) => Inspection(device) && _policy.InstalledAppListingEnabled && device.Device.Capabilities.AllowInstalledAppListing;
    public bool Diagnostic(ConfiguredDevice device, DiagnosticKind diagnostic) =>
        Inspection(device) && _policy.DiagnosticsEnabled && device.Device.Capabilities.AllowDiagnostics && _policy.AllowedDiagnostics.Contains(diagnostic);
    public bool MediaInspection(ConfiguredDevice device) => Inspection(device) && _policy.MediaInspectionEnabled && device.Device.Capabilities.AllowMediaInspection;
    public bool MediaMetadata(ConfiguredDevice device) => MediaInspection(device) && _policy.MediaMetadataEnabled && device.Device.Capabilities.AllowMediaMetadata;
    public bool MediaControl(ConfiguredDevice device, MediaAction action) => Inspection(device) && _policy.MediaControlEnabled && device.Device.Capabilities.AllowMediaControl && _policy.AllowedMediaActions.Contains(action);
    public bool VolumeControl(ConfiguredDevice device, VolumeAction action) => Inspection(device) && _policy.VolumeControlEnabled && device.Device.Capabilities.AllowVolumeControl && _policy.AllowedVolumeActions.Contains(action);
    public bool PackageInstall(ConfiguredDevice device) => Inspection(device) && _policy.PackageInstallEnabled && device.Device.Capabilities.AllowPackageInstall;
    public bool PackageUninstall(ConfiguredDevice device) => Inspection(device) && _policy.PackageUninstallEnabled && device.Device.Capabilities.AllowPackageUninstall;
    public bool ArbitraryCommands(ConfiguredDevice device) => Inspection(device) && _policy.ArbitraryCommandsEnabled && device.Device.Capabilities.AllowArbitraryCommands;
    public bool Power(ConfiguredDevice device) => Inspection(device) && _policy.PowerControlEnabled && device.Device.Capabilities.AllowPower;
    public bool Navigation(ConfiguredDevice device, NavigationAction action) => Inspection(device) && _policy.NavigationControlEnabled && device.Device.Capabilities.AllowNavigation && _policy.AllowedNavigationActions.Contains(action);
    public bool AppLaunch(ConfiguredDevice device) => Inspection(device) && _policy.AppLaunchEnabled && device.Device.Capabilities.AllowAppLaunch;
    public bool AppStop(ConfiguredDevice device) => Inspection(device) && _policy.AppStopEnabled && device.Device.Capabilities.AllowAppStop;

    public CapabilityStatus Describe(ConfiguredDevice device) => new(
        device.Alias, Inspection(device),
        Inspection(device) && _policy.DiagnosticsEnabled && device.Device.Capabilities.AllowDiagnostics,
        InstalledApps(device), MediaInspection(device), MediaMetadata(device),
        Inspection(device) && _policy.MediaControlEnabled && device.Device.Capabilities.AllowMediaControl,
        Inspection(device) && _policy.VolumeControlEnabled && device.Device.Capabilities.AllowVolumeControl,
        PackageInstall(device), PackageUninstall(device), ArbitraryCommands(device), Power(device),
        Inspection(device) && _policy.NavigationControlEnabled && device.Device.Capabilities.AllowNavigation,
        AppLaunch(device), AppStop(device),
        Inspection(device) && _policy.DiagnosticsEnabled && device.Device.Capabilities.AllowDiagnostics
            ? _policy.AllowedDiagnostics.Order().Select(x => x.ToString()).ToArray() : [],
        Inspection(device) && _policy.NavigationControlEnabled && device.Device.Capabilities.AllowNavigation
            ? _policy.AllowedNavigationActions.Order().Select(x => x.ToString()).ToArray() : [],
        Inspection(device) && _policy.MediaControlEnabled && device.Device.Capabilities.AllowMediaControl
            ? _policy.AllowedMediaActions.Order().Select(x => x.ToString()).ToArray() : [],
        Inspection(device) && _policy.VolumeControlEnabled && device.Device.Capabilities.AllowVolumeControl
            ? _policy.AllowedVolumeActions.Order().Select(x => x.ToString()).ToArray() : []);
}
