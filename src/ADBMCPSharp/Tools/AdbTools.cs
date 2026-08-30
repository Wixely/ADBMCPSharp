using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ADBMCPSharp.Configuration;
using ADBMCPSharp.Services;
using ModelContextProtocol.Server;

namespace ADBMCPSharp.Tools;

[McpServerToolType]
public static class AdbTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [McpServerTool(Name = "adb_list_devices"),
     Description("List configured Android device aliases without exposing ADB selectors, serials, or addresses.")]
    public static string ListDevices(AndroidDeviceService service) => JsonSerializer.Serialize(service.ListDevices(), JsonOptions);

    [McpServerTool(Name = "adb_list_adb_servers"),
     Description("List configured ADB server aliases, local/remote modes, and the effective passive-discovery gate without exposing hosts or ports.")]
    public static string ListAdbServers(AdbDiscoveryService service) => JsonSerializer.Serialize(service.ListServers(), JsonOptions);

    [McpServerTool(Name = "adb_discover_devices"),
     Description("Passively discover bounded ADB mDNS advertisements visible to a configured ADB server. Disabled by default; returns redacted opaque handles without addresses, ports, serial-derived names, or pairing data, and never issues pair or connect commands.")]
    public static async Task<string> DiscoverDevices(
        AdbDiscoveryService service,
        [Description("Operator-defined ADB server alias")] string serverAlias,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.DiscoverAsync(serverAlias, cancellationToken), JsonOptions);

    [McpServerTool(Name = "adb_get_device_status"),
     Description("Inspect bounded connection, Android, power/display, and foreground-application state for a configured device alias.")]
    public static async Task<string> GetDeviceStatus(
        AndroidDeviceService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.GetStatusAsync(deviceAlias, cancellationToken), JsonOptions);

    [McpServerTool(Name = "adb_get_connection_health"),
     Description("Inspect redacted ADB connection health for a configured device alias without exposing its selector or network endpoint.")]
    public static async Task<string> GetConnectionHealth(
        AdbConnectionService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.GetHealthAsync(deviceAlias, cancellationToken), JsonOptions);

    [McpServerTool(Name = "adb_connect_device"),
     Description("Connect one configured device through its configured ADB server. Independently gated and requires explicit confirmation; no endpoint is accepted from MCP.")]
    public static async Task<string> ConnectDevice(
        AdbConnectionService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        [Description("Must be true to confirm the connection change")] bool confirmChange,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.ConnectAsync(deviceAlias, confirmChange, cancellationToken), JsonOptions);

    [McpServerTool(Name = "adb_reconnect_device"),
     Description("Disconnect and reconnect one configured device using only its server-side profile. Independently gated and requires explicit confirmation.")]
    public static async Task<string> ReconnectDevice(
        AdbConnectionService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        [Description("Must be true to confirm the connection change")] bool confirmChange,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.ReconnectAsync(deviceAlias, confirmChange, cancellationToken), JsonOptions);

    [McpServerTool(Name = "adb_disconnect_device"),
     Description("Disconnect one configured device from its configured ADB server. Independently gated and requires explicit confirmation; no endpoint is accepted from MCP.")]
    public static async Task<string> DisconnectDevice(
        AdbConnectionService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        [Description("Must be true to confirm the connection change")] bool confirmChange,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.DisconnectAsync(deviceAlias, confirmChange, cancellationToken), JsonOptions);

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

    [McpServerTool(Name = "adb_list_installed_apps"),
     Description("List a bounded set of installed Android package identifiers for a configured device. Privacy-sensitive and disabled by default; scope is restricted to All, User, or System.")]
    public static async Task<string> ListInstalledApps(
        AndroidDeviceService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        [Description("Package scope: All, User, or System")] InstalledAppScope scope,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.ListInstalledAppsAsync(deviceAlias, scope, cancellationToken), JsonOptions);

    [McpServerTool(Name = "adb_list_diagnostics"),
     Description("List the curated read-only diagnostic options and their effective enablement for one configured device.")]
    public static string ListDiagnostics(
        DeviceDiagnosticService service,
        [Description("Operator-defined device alias")] string deviceAlias) =>
        JsonSerializer.Serialize((object?)service.List(deviceAlias) ?? new { error = "Unknown device alias." }, JsonOptions);

    [McpServerTool(Name = "adb_run_diagnostic"),
     Description("Run one enabled, read-only, structured diagnostic: Battery, Memory, Storage, CpuLoad, Runtime, Display, or Security. Raw ADB output is never returned.")]
    public static async Task<string> RunDiagnostic(
        DeviceDiagnosticService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        [Description("Curated diagnostic option")] DiagnosticKind diagnostic,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.RunAsync(deviceAlias, diagnostic, cancellationToken), JsonOptions);

    [McpServerTool(Name = "adb_get_media_status"),
     Description("Inspect the active Android media session, playback state, position, speed, and bounded title/artist metadata. Privacy-sensitive and disabled by default; unrecognized package identifiers are redacted.")]
    public static async Task<string> GetMediaStatus(
        AndroidDeviceService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.GetMediaStatusAsync(deviceAlias, cancellationToken), JsonOptions);

    [McpServerTool(Name = "adb_send_media_action"),
     Description("Send one operator-allowlisted media action: Play, Pause, PlayPause, Stop, Next, Previous, FastForward, or Rewind.")]
    public static async Task<string> SendMediaAction(
        AndroidDeviceService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        [Description("Bounded media action")] MediaAction action,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.SendMediaActionAsync(deviceAlias, action, cancellationToken), JsonOptions);

    [McpServerTool(Name = "adb_send_volume_action"),
     Description("Send one operator-allowlisted volume action: Up, Down, or Mute.")]
    public static async Task<string> SendVolumeAction(
        AndroidDeviceService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        [Description("Bounded volume action")] VolumeAction action,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.SendVolumeActionAsync(deviceAlias, action, cancellationToken), JsonOptions);

    [McpServerTool(Name = "adb_list_installable_apks"),
     Description("List operator-configured APK artifact aliases available to a device without exposing local paths, download URLs, hashes, or package identifiers.")]
    public static string ListInstallableApks(
        PackageAdministrationService service,
        [Description("Operator-defined device alias")] string deviceAlias) =>
        JsonSerializer.Serialize((object?)service.ListInstallableApks(deviceAlias) ?? new { error = "Unknown device alias." }, JsonOptions);

    [McpServerTool(Name = "adb_install_apk"),
     Description("Install one checksum-pinned, operator-configured APK artifact on an explicitly enabled device. Requires explicit package-change confirmation.")]
    public static async Task<string> InstallApk(
        PackageAdministrationService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        [Description("Operator-defined APK artifact alias")] string artifactAlias,
        [Description("Must be true to confirm the installation")] bool confirmChange,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.InstallAsync(deviceAlias, artifactAlias, confirmChange, cancellationToken), JsonOptions);

    [McpServerTool(Name = "adb_uninstall_app"),
     Description("Uninstall one operator-allowlisted application with its independent uninstall flag enabled. Requires explicit package-change confirmation.")]
    public static async Task<string> UninstallApp(
        PackageAdministrationService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        [Description("Operator-defined allowlisted application alias")] string appAlias,
        [Description("Must be true to confirm the removal")] bool confirmChange,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.UninstallAsync(deviceAlias, appAlias, confirmChange, cancellationToken), JsonOptions);

    [McpServerTool(Name = "adb_execute_arbitrary_command"),
     Description("BREAK-GLASS: execute arbitrary device-scoped ADB arguments on one configured device. Disabled by default, independently gated per device, bounded, audited without argument content, and requires explicit high-impact confirmation. Output may contain sensitive device data.")]
    public static async Task<string> ExecuteArbitrary(
        ArbitraryAdbService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        [Description("ADB arguments after the fixed device selector; for example: [\"shell\", \"getprop\", \"ro.build.type\"]")] IReadOnlyList<string> arguments,
        [Description("Must be true to acknowledge unrestricted device impact and potentially sensitive output")] bool confirmHighImpact,
        CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.ExecuteAsync(deviceAlias, arguments, confirmHighImpact, cancellationToken), JsonOptions);

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
     Description("Send one bounded navigation action that is explicitly allowlisted by the operator.")]
    public static async Task<string> SendNavigation(
        AndroidDeviceService service,
        [Description("Operator-defined device alias")] string deviceAlias,
        [Description("Bounded action: Home, Back, Up, Down, Left, Right, Select, or Menu")]
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
