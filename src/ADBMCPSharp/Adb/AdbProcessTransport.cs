using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using ADBMCPSharp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ADBMCPSharp.Adb;

public sealed class AdbProcessTransport(
    IOptions<AdbOptions> options,
    ILogger<AdbProcessTransport> logger) : IAdbTransport
{
    private const int MaxCapturedCharacters = 65_536;
    private readonly AdbOptions _options = options.Value;

    public async Task<AdbExecutionResult> ExecuteAsync(
        AdbServerOptions server,
        string deviceSelector,
        AdbRequest request,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (server.Mode == AdbServerMode.Remote)
        {
            start.ArgumentList.Add("-H");
            start.ArgumentList.Add(server.Host!);
            start.ArgumentList.Add("-P");
            start.ArgumentList.Add(server.Port.ToString(CultureInfo.InvariantCulture));
        }

        start.ArgumentList.Add("-s");
        start.ArgumentList.Add(deviceSelector);
        AddFixedRequestArguments(start.ArgumentList, request);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.CommandTimeoutSeconds));
        using var process = new Process { StartInfo = start };

        try
        {
            if (!process.Start()) return new(false, string.Empty, AdbFailureKind.Unavailable, "ADB could not be started.");
            var stdoutTask = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var stderrTask = ReadBoundedAsync(process.StandardError, timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await stdoutTask.ConfigureAwait(false);
            var error = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode == 0) return new(true, output.Trim());
            return ClassifyFailure(output, error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            logger.LogWarning("ADB operation {Operation} timed out", request.Kind);
            return new(false, string.Empty, AdbFailureKind.TimedOut, "The ADB operation timed out.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new(false, string.Empty, AdbFailureKind.Cancelled, "The ADB operation was cancelled.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogError("ADB executable is unavailable for operation {Operation}", request.Kind);
            return new(false, string.Empty, AdbFailureKind.Unavailable, "The configured ADB executable is unavailable.");
        }
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
            case AdbRequestKind.GetForegroundWindow: Add(args, "shell", "dumpsys", "window", "windows"); break;
            case AdbRequestKind.GetPackagePath: Add(args, "shell", "pm", "path", RequiredValue(request)); break;
            case AdbRequestKind.GetProcessId: Add(args, "shell", "pidof", RequiredValue(request)); break;
            case AdbRequestKind.Wake: Add(args, "shell", "input", "keyevent", "KEYCODE_WAKEUP"); break;
            case AdbRequestKind.Sleep: Add(args, "shell", "input", "keyevent", "KEYCODE_SLEEP"); break;
            case AdbRequestKind.Navigation: Add(args, "shell", "input", "keyevent", ToKeyCode(RequiredValue(request))); break;
            case AdbRequestKind.LaunchPackage:
                Add(args, "shell", "monkey", "-p", RequiredValue(request), "-c", "android.intent.category.LAUNCHER", "1");
                break;
            case AdbRequestKind.StopPackage: Add(args, "shell", "am", "force-stop", RequiredValue(request)); break;
            default: throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private static string RequiredValue(AdbRequest request) =>
        !string.IsNullOrWhiteSpace(request.Value) ? request.Value : throw new ArgumentException("Request value is required.");

    private static string ToKeyCode(string action) => Enum.Parse<NavigationAction>(action) switch
    {
        NavigationAction.Home => "KEYCODE_HOME",
        NavigationAction.Back => "KEYCODE_BACK",
        NavigationAction.Up => "KEYCODE_DPAD_UP",
        NavigationAction.Down => "KEYCODE_DPAD_DOWN",
        NavigationAction.Left => "KEYCODE_DPAD_LEFT",
        NavigationAction.Right => "KEYCODE_DPAD_RIGHT",
        NavigationAction.Select => "KEYCODE_DPAD_CENTER",
        NavigationAction.Menu => "KEYCODE_MENU",
        NavigationAction.PlayPause => "KEYCODE_MEDIA_PLAY_PAUSE",
        NavigationAction.Next => "KEYCODE_MEDIA_NEXT",
        NavigationAction.Previous => "KEYCODE_MEDIA_PREVIOUS",
        NavigationAction.VolumeUp => "KEYCODE_VOLUME_UP",
        NavigationAction.VolumeDown => "KEYCODE_VOLUME_DOWN",
        NavigationAction.Mute => "KEYCODE_VOLUME_MUTE",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
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
            if (result.Length < MaxCapturedCharacters)
            {
                var remaining = MaxCapturedCharacters - result.Length;
                result.AppendLine(line.Length <= remaining ? line : line[..remaining]);
            }
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
