using ADBMCPSharp.Configuration;
using ADBMCPSharp.Models;
using Microsoft.Extensions.Options;

namespace ADBMCPSharp.Services;

public sealed class CapabilityPolicy(IOptions<PolicyOptions> options)
{
    private readonly PolicyOptions _policy = options.Value;

    public bool Inspection(ConfiguredDevice device) => _policy.InspectionEnabled && device.Device.Capabilities.Enabled;
    public bool Power(ConfiguredDevice device) => _policy.PowerControlEnabled && device.Device.Capabilities.AllowPower;
    public bool Navigation(ConfiguredDevice device, NavigationAction action) =>
        _policy.NavigationControlEnabled && device.Device.Capabilities.AllowNavigation &&
        _policy.AllowedNavigationActions.Contains(action);
    public bool AppLaunch(ConfiguredDevice device) => _policy.AppLaunchEnabled && device.Device.Capabilities.AllowAppLaunch;
    public bool AppStop(ConfiguredDevice device) => _policy.AppStopEnabled && device.Device.Capabilities.AllowAppStop;

    public CapabilityStatus Describe(ConfiguredDevice device) => new(
        device.Alias,
        Inspection(device),
        Power(device),
        _policy.NavigationControlEnabled && device.Device.Capabilities.AllowNavigation,
        AppLaunch(device),
        AppStop(device),
        _policy.NavigationControlEnabled && device.Device.Capabilities.AllowNavigation
            ? _policy.AllowedNavigationActions.Order().Select(x => x.ToString()).ToArray()
            : []);
}
