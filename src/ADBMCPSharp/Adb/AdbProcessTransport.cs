using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using ADBMCPSharp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ADBMCPSharp.Adb;

public sealed class AdbProcessTransport(IOptions<AdbOptions> options, ILogger<AdbProcessTransport> logger) : IAdbTransport
{
    private const int MaxCapturedCharacters = 65_536;
    private const string DefaultExecutableName = "adb";
    private readonly AdbOptions _options = options.Value;

    public Task<AdbExecutionResult> ExecuteServerAsync(
        AdbServerOptions server, AdbServerRequest request, CancellationToken cancellationToken) =>
        ExecuteCoreAsync(request.ToString(), BuildServerArguments(server, request), _options.CommandTimeoutSeconds, cancellationToken);

    public Task<AdbExecutionResult> ExecuteAsync(
        AdbServerOptions server, string deviceSelector, AdbRequest request, CancellationToken cancellationToken) =>
        ExecuteCoreAsync(
            request.Kind.ToString(),
            BuildDeviceArguments(server, deviceSelector, request),
            request.TimeoutSeconds ?? _options.CommandTimeoutSeconds,
            cancellationToken);

    public Task<AdbExecutionResult> ExecuteConnectionAsync(
        AdbServerOptions server, string deviceSelector, AdbConnectionRequest request, CancellationToken cancellationToken) =>
        ExecuteCoreAsync(
            request.ToString(),
            BuildConnectionArguments(server, deviceSelector, request),
            _options.ConnectionOperationTimeoutSeconds,
            cancellationToken);

    private async Task<AdbExecutionResult> ExecuteCoreAsync(
        string operation, IReadOnlyList<string> arguments, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = ResolveExecutablePath(_options.ExecutablePath),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var process = new Process { StartInfo = start };

        try
        {
            if (!process.Start()) return new(false, string.Empty, AdbFailureKind.Unavailable, "ADB could not be started.");
            var stdoutTask = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var stderrTask = ReadBoundedAsync(process.StandardError, timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await stdoutTask.ConfigureAwait(false);
            var error = await stderrTask.ConfigureAwait(false);
            return process.ExitCode == 0 ? new(true, output.Trim()) : ClassifyFailure(output, error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            logger.LogWarning("ADB operation {Operation} timed out", operation);
            return new(false, string.Empty, AdbFailureKind.TimedOut, "The ADB operation timed out.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new(false, string.Empty, AdbFailureKind.Cancelled, "The ADB operation was cancelled.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogError("ADB executable is unavailable for operation {Operation}", operation);
            return new(false, string.Empty, AdbFailureKind.Unavailable, "The configured ADB executable is unavailable.");
        }
    }

    internal static string ResolveExecutablePath(string? configuredPath) =>
        string.IsNullOrWhiteSpace(configuredPath) ? DefaultExecutableName : configuredPath;

    internal static IReadOnlyList<string> BuildServerArguments(AdbServerOptions server, AdbServerRequest request)
    {
        var arguments = BuildBaseArguments(server);
        switch (request)
        {
            case AdbServerRequest.CheckMdns: Add(arguments, "mdns", "check"); break;
            case AdbServerRequest.ListMdnsServices: Add(arguments, "mdns", "services"); break;
            default: throw new ArgumentOutOfRangeException(nameof(request));
        }
        return arguments;
    }

    internal static IReadOnlyList<string> BuildDeviceArguments(AdbServerOptions server, string deviceSelector, AdbRequest request)
    {
        var arguments = BuildBaseArguments(server);
        arguments.Add("-s");
        arguments.Add(deviceSelector);
        AddFixedRequestArguments(arguments, request);
        return arguments;
    }

    internal static IReadOnlyList<string> BuildConnectionArguments(
        AdbServerOptions server, string deviceSelector, AdbConnectionRequest request)
    {
        var arguments = BuildBaseArguments(server);
        arguments.Add(request switch
        {
            AdbConnectionRequest.Connect => "connect",
            AdbConnectionRequest.Disconnect => "disconnect",
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        });
        arguments.Add(deviceSelector);
        return arguments;
    }

    private static Collection<string> BuildBaseArguments(AdbServerOptions server)
    {
        var arguments = new Collection<string>();
        if (server.Mode == AdbServerMode.Remote)
        {
            Add(arguments, "-H", server.Host!, "-P", server.Port.ToString(CultureInfo.InvariantCulture));
        }
        return arguments;
    }

    private static void AddFixedRequestArguments(Collection<string> args, AdbRequest request)
    {
        switch (request.Kind)
        {
            case AdbRequestKind.GetState: Add(args, "get-state"); break;
            case AdbRequestKind.GetManufacturer: Add(args, "shell", "getprop", "ro.product.manufacturer"); break;
            case AdbRequestKind.GetModel: Add(args, "shell", "getprop", "ro.product.model"); break;
            case AdbRequestKind.GetAndroidVersion: Add(args, "shell", "getprop", "ro.build.version.release"); break;
            case AdbRequestKind.GetApiLevel: Add(args, "shell", "getprop", "ro.build.version.sdk"); break;
            case AdbRequestKind.GetPowerState: Add(args, "shell", "dumpsys", "power"); break;
            case AdbRequestKind.GetForegroundWindow: Add(args, "shell", "dumpsys", "window"); break;
            case AdbRequestKind.GetDreamState: Add(args, "shell", "dumpsys", "dreams"); break;
            case AdbRequestKind.GetPackagePath: Add(args, "shell", "pm", "path", RequiredValue(request)); break;
            case AdbRequestKind.GetProcessId: Add(args, "shell", "pidof", RequiredValue(request)); break;
            case AdbRequestKind.ListInstalledPackages:
                Add(args, "shell", "pm", "list", "packages");
                var scope = ToPackageScopeArgument(RequiredValue(request));
                if (scope is not null) args.Add(scope);
                break;
            case AdbRequestKind.GetBatteryDiagnostic: Add(args, "shell", "dumpsys", "battery"); break;
            case AdbRequestKind.GetMemoryDiagnostic: Add(args, "shell", "cat", "/proc/meminfo"); break;
            case AdbRequestKind.GetStorageDiagnostic: Add(args, "shell", "df", "-k", "/data"); break;
            case AdbRequestKind.GetCpuLoadDiagnostic: Add(args, "shell", "cat", "/proc/loadavg"); break;
            case AdbRequestKind.GetRuntimeDiagnostic: Add(args, "shell", "cat", "/proc/uptime"); break;
            case AdbRequestKind.GetDisplaySizeDiagnostic: Add(args, "shell", "wm", "size"); break;
            case AdbRequestKind.GetDisplayDensityDiagnostic: Add(args, "shell", "wm", "density"); break;
            case AdbRequestKind.GetBuildTypeDiagnostic: Add(args, "shell", "getprop", "ro.build.type"); break;
            case AdbRequestKind.GetDebuggableDiagnostic: Add(args, "shell", "getprop", "ro.debuggable"); break;
            case AdbRequestKind.GetSecureDiagnostic: Add(args, "shell", "getprop", "ro.secure"); break;
            case AdbRequestKind.GetAdbSecureDiagnostic: Add(args, "shell", "getprop", "ro.adb.secure"); break;
            case AdbRequestKind.GetVerifiedBootDiagnostic: Add(args, "shell", "getprop", "ro.boot.verifiedbootstate"); break;
            case AdbRequestKind.GetFlashLockedDiagnostic: Add(args, "shell", "getprop", "ro.boot.flash.locked"); break;
            case AdbRequestKind.GetSelinuxDiagnostic: Add(args, "shell", "getenforce"); break;
            case AdbRequestKind.GetMediaSession: Add(args, "shell", "dumpsys", "media_session"); break;
            case AdbRequestKind.MediaAction: Add(args, "shell", "input", "keyevent", ToMediaKeyCode(RequiredValue(request))); break;
            case AdbRequestKind.VolumeAction: Add(args, "shell", "input", "keyevent", ToVolumeKeyCode(RequiredValue(request))); break;
            case AdbRequestKind.InstallApk:
                Add(args, "install");
                if (request.Flag) args.Add("-r");
                args.Add(RequiredValue(request));
                break;
            case AdbRequestKind.UninstallPackage: Add(args, "uninstall", RequiredValue(request)); break;
            case AdbRequestKind.ArbitraryDeviceCommand:
                if (request.Arguments is not { Count: > 0 }) throw new ArgumentException("Arbitrary device arguments are required.");
                foreach (var argument in request.Arguments) args.Add(argument);
                break;
            case AdbRequestKind.Wake: Add(args, "shell", "input", "keyevent", "KEYCODE_WAKEUP"); break;
            case AdbRequestKind.Sleep: Add(args, "shell", "input", "keyevent", "KEYCODE_SLEEP"); break;
            case AdbRequestKind.StopDreaming: Add(args, "shell", "cmd", "dreams", "stop-dreaming"); break;
            case AdbRequestKind.Navigation: Add(args, "shell", "input", "keyevent", ToNavigationKeyCode(RequiredValue(request))); break;
            case AdbRequestKind.LaunchPackage:
                Add(args, "shell", "monkey", "-p", RequiredValue(request), "-c",
                    request.Flag ? "android.intent.category.LEANBACK_LAUNCHER" : "android.intent.category.LAUNCHER", "1");
                break;
            case AdbRequestKind.StopPackage: Add(args, "shell", "am", "force-stop", RequiredValue(request)); break;
            default: throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private static string RequiredValue(AdbRequest request) =>
        !string.IsNullOrWhiteSpace(request.Value) ? request.Value : throw new ArgumentException("Request value is required.");

    private static string? ToPackageScopeArgument(string value) => Enum.Parse<InstalledAppScope>(value) switch
    {
        InstalledAppScope.All => null,
        InstalledAppScope.User => "-3",
        InstalledAppScope.System => "-s",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string ToNavigationKeyCode(string value) => Enum.Parse<NavigationAction>(value) switch
    {
        NavigationAction.Home => "KEYCODE_HOME",
        NavigationAction.Back => "KEYCODE_BACK",
        NavigationAction.Up => "KEYCODE_DPAD_UP",
        NavigationAction.Down => "KEYCODE_DPAD_DOWN",
        NavigationAction.Left => "KEYCODE_DPAD_LEFT",
        NavigationAction.Right => "KEYCODE_DPAD_RIGHT",
        NavigationAction.Select => "KEYCODE_DPAD_CENTER",
        NavigationAction.Menu => "KEYCODE_MENU",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string ToMediaKeyCode(string value) => Enum.Parse<MediaAction>(value) switch
    {
        MediaAction.Play => "KEYCODE_MEDIA_PLAY",
        MediaAction.Pause => "KEYCODE_MEDIA_PAUSE",
        MediaAction.PlayPause => "KEYCODE_MEDIA_PLAY_PAUSE",
        MediaAction.Stop => "KEYCODE_MEDIA_STOP",
        MediaAction.Next => "KEYCODE_MEDIA_NEXT",
        MediaAction.Previous => "KEYCODE_MEDIA_PREVIOUS",
        MediaAction.Rewind => "KEYCODE_MEDIA_REWIND",
        MediaAction.FastForward => "KEYCODE_MEDIA_FAST_FORWARD",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string ToVolumeKeyCode(string value) => Enum.Parse<VolumeAction>(value) switch
    {
        VolumeAction.Up => "KEYCODE_VOLUME_UP",
        VolumeAction.Down => "KEYCODE_VOLUME_DOWN",
        VolumeAction.Mute => "KEYCODE_VOLUME_MUTE",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static void Add(Collection<string> args, params string[] values)
    {
        foreach (var value in values) args.Add(value);
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (result.Length >= MaxCapturedCharacters) continue;
            var remaining = MaxCapturedCharacters - result.Length;
            result.AppendLine(line.Length <= remaining ? line : line[..remaining]);
        }
        return result.ToString();
    }

    private static AdbExecutionResult ClassifyFailure(string output, string error)
    {
        var combined = string.Concat(output, "\n", error);
        if (combined.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
            return new(false, string.Empty, AdbFailureKind.Unauthorized, "The device has not authorized this ADB server.");
        if (combined.Contains("offline", StringComparison.OrdinalIgnoreCase))
            return new(false, string.Empty, AdbFailureKind.Offline, "The device is offline.");
        if (combined.Contains("not found", StringComparison.OrdinalIgnoreCase) || combined.Contains("no devices", StringComparison.OrdinalIgnoreCase))
            return new(false, string.Empty, AdbFailureKind.Unavailable, "The configured device is unavailable.");
        return new(false, string.Empty, AdbFailureKind.Failed, "ADB rejected the bounded operation.");
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}
