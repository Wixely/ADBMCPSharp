using System.ComponentModel;
using System.Text.Json;
using ADBMCPSharp.Configuration;
using ADBMCPSharp.Services;
using ModelContextProtocol.Server;

namespace ADBMCPSharp.Tools;

[McpServerToolType]
public static class AdbTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [McpServerTool(Name = "adb_list_devices"),
     Description("List configured Android device aliases without exposing ADB selectors, serials, or addresses.")]
    public static string ListDevices(AndroidDeviceService service) => JsonSerializer.Serialize(service.ListDevices(), JsonOptions);

    [McpServerTool(Name = "adb_get_device_status"),
     Description("Inspect bounded connection, Android, power/display, and foreground-application state for a configured device alias.")]
    public static async Task<string> GetDeviceStatus(
        AndroidDeviceService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.GetStatusAsync(deviceAlias, cancellationToken), JsonOptions);

    [McpServerTool(Name = "adb_list_allowed_apps"),
     Description("List operator-configured application aliases and effective launch/stop gates for one device.")]
    public static string ListAllowedApps(
        AndroidDeviceService service,
        [Description("Operator-defined device alias")] string deviceAlias) =>
        JsonSerializer.Serialize((object?)service.ListAllowedApps(deviceAlias) ?? new { error = "Unknown device alias." }, JsonOptions);

    [McpServerTool(Name = "adb_get_app_status"),
     Description("Inspect installed, running, and foreground state for an allowlisted application alias.")]
    public static async Task<string> GetAppStatus(
        AndroidDeviceService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        [Description("Operator-defined allowlisted application alias")] string appAlias,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.GetAppStatusAsync(deviceAlias, appAlias, cancellationToken), JsonOptions);

    [McpServerTool(Name = "adb_get_capabilities"),
     Description("Get the effective inspection and control gates for one configured device.")]
    public static string GetCapabilities(
        AndroidDeviceService service,
        [Description("Operator-defined device alias")] string deviceAlias) =>
        JsonSerializer.Serialize((object?)service.GetCapabilities(deviceAlias) ?? new { error = "Unknown device alias." }, JsonOptions);

    [McpServerTool(Name = "adb_wake_device"),
     Description("Wake a configured device when power control is explicitly enabled, then verify observable power state.")]
    public static async Task<string> WakeDevice(
        AndroidDeviceService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.WakeAsync(deviceAlias, cancellationToken), JsonOptions);

    [McpServerTool(Name = "adb_sleep_device"),
     Description("Sleep a configured device when power control is explicitly enabled, then verify observable power state.")]
    public static async Task<string> SleepDevice(
        AndroidDeviceService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.SleepAsync(deviceAlias, cancellationToken), JsonOptions);

    [McpServerTool(Name = "adb_send_navigation"),
     Description("Send one bounded navigation, media, or volume action that is explicitly allowlisted by the operator.")]
    public static async Task<string> SendNavigation(
        AndroidDeviceService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        [Description("Bounded action: Home, Back, Up, Down, Left, Right, Select, Menu, PlayPause, Next, Previous, VolumeUp, VolumeDown, or Mute")]
        NavigationAction action,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.NavigateAsync(deviceAlias, action, cancellationToken), JsonOptions);

    [McpServerTool(Name = "adb_launch_app"),
     Description("Launch one operator-allowlisted application when application launch is explicitly enabled, then verify foreground state.")]
    public static async Task<string> LaunchApp(
        AndroidDeviceService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        [Description("Operator-defined allowlisted application alias")] string appAlias,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.LaunchAppAsync(deviceAlias, appAlias, cancellationToken), JsonOptions);

    [McpServerTool(Name = "adb_stop_app"),
     Description("Force-stop one operator-allowlisted application when the separate stop gate is explicitly enabled, then verify process state.")]
    public static async Task<string> StopApp(
        AndroidDeviceService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        [Description("Operator-defined allowlisted application alias")] string appAlias,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.StopAppAsync(deviceAlias, appAlias, cancellationToken), JsonOptions);
}
