using System.Globalization;
using System.Text.RegularExpressions;

namespace ADBMCPSharp.Services;

internal static partial class MediaSessionParser
{
    [GeneratedRegex(@"^\s*Media button session is[^\r\n]*?\b(?<package>[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+)\b", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.NonBacktracking)]
    private static partial Regex PackageRegex();

    [GeneratedRegex(@"state=PlaybackState\s*\{state=(?<state>\d+),\s*position=(?<position>-?\d+).*?speed=(?<speed>-?[0-9.]+)", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex PlaybackRegex();

    public static ParsedMediaSession Parse(string output)
    {
        var packageMatch = PackageRegex().Match(output);
        var playbackMatch = PlaybackRegex().Match(output);
        var state = playbackMatch.Success && int.TryParse(playbackMatch.Groups["state"].Value, out var stateNumber)
            ? MapPlaybackState(stateNumber)
            : null;
        long? position = playbackMatch.Success && long.TryParse(playbackMatch.Groups["position"].Value, out var positionValue)
            ? positionValue
            : null;
        double? speed = playbackMatch.Success && double.TryParse(
            playbackMatch.Groups["speed"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var speedValue)
            ? speedValue
            : null;

        return new(
            packageMatch.Success ? packageMatch.Groups["package"].Value : null,
            output.Contains("active=true", StringComparison.OrdinalIgnoreCase) ? true :
                output.Contains("active=false", StringComparison.OrdinalIgnoreCase) ? false : null,
            state,
            position,
            speed,
            ReadMetadata(output, "android.media.metadata.TITLE="),
            ReadMetadata(output, "android.media.metadata.ARTIST="),
            ReadMetadata(output, "android.media.metadata.ALBUM="));
    }

    private static string? ReadMetadata(string output, string marker)
    {
        var index = output.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        var start = index + marker.Length;
        var end = output.IndexOfAny(['\r', '\n'], start);
        if (end < 0) end = output.Length;
        var value = new string(output[start..end].Where(c => !char.IsControl(c)).Take(256).ToArray()).Trim();
        return value.Length == 0 ? null : value;
    }

    private static string MapPlaybackState(int state) => state switch
    {
        0 => "None",
        1 => "Stopped",
        2 => "Paused",
        3 => "Playing",
        4 => "FastForwarding",
        5 => "Rewinding",
        6 => "Buffering",
        7 => "Error",
        8 => "Connecting",
        9 => "SkippingToPrevious",
        10 => "SkippingToNext",
        11 => "SkippingToQueueItem",
        _ => "Unknown",
    };
}

internal sealed record ParsedMediaSession(
    string? Package,
    bool? Active,
    string? PlaybackState,
    long? PositionMilliseconds,
    double? Speed,
    string? Title,
    string? Artist,
    string? Album);
