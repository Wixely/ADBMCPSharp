using System.Globalization;
using System.Text.RegularExpressions;
using ADBMCPSharp.Models;

namespace ADBMCPSharp.Services;

internal static partial class DiagnosticOutputParser
{
    [GeneratedRegex(@"(?<width>\d+)x(?<height>\d+)", RegexOptions.NonBacktracking)]
    private static partial Regex DimensionsRegex();

    public static BatteryDiagnostic ParseBattery(string output)
    {
        var values = ParseColonValues(output);
        var scale = Integer(values, "scale");
        var level = Integer(values, "level");
        var levelPercent = level is not null && scale is > 0 ? (int?)Math.Clamp(level.Value * 100 / scale.Value, 0, 100) : null;
        return new(
            levelPercent,
            MapBatteryStatus(Integer(values, "status")),
            MapBatteryHealth(Integer(values, "health")),
            Boolean(values, "AC powered") == true,
            Boolean(values, "USB powered") == true,
            Boolean(values, "Wireless powered") == true,
            Integer(values, "temperature"),
            Integer(values, "voltage"));
    }

    public static MemoryDiagnostic ParseMemory(string output)
    {
        var values = ParseMemoryValues(output);
        return new(
            Value(values, "MemTotal"), Value(values, "MemAvailable"), Value(values, "MemFree"),
            Value(values, "Buffers"), Value(values, "Cached"), Value(values, "SwapTotal"), Value(values, "SwapFree"));
    }

    public static StorageDiagnostic ParseStorage(string output)
    {
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || !parts[^2].EndsWith('%')) continue;
            if (!long.TryParse(parts[^5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total) ||
                !long.TryParse(parts[^4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var used) ||
                !long.TryParse(parts[^3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var available) ||
                !int.TryParse(parts[^2][..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent)) continue;
            return new(total, used, available, Math.Clamp(percent, 0, 100));
        }
        return new(null, null, null, null);
    }

    public static CpuLoadDiagnostic ParseCpuLoad(string output)
    {
        var parts = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return new(ParseDouble(parts, 0), ParseDouble(parts, 1), ParseDouble(parts, 2));
    }

    public static RuntimeDiagnostic ParseRuntime(string output)
    {
        var parts = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return new(ParseDouble(parts, 0), ParseDouble(parts, 1));
    }

    public static DisplayDiagnostic ParseDisplay(string sizeOutput, string densityOutput)
    {
        var physicalSize = ReadDimensions(sizeOutput, "Physical size:");
        var overrideSize = ReadDimensions(sizeOutput, "Override size:");
        return new(
            physicalSize.Width, physicalSize.Height, overrideSize.Width, overrideSize.Height,
            ReadTrailingInteger(densityOutput, "Physical density:"),
            ReadTrailingInteger(densityOutput, "Override density:"));
    }

    public static bool? ParseBooleanProperty(string output) => output.Trim() switch
    {
        "1" or "true" => true,
        "0" or "false" => false,
        _ => null,
    };

    public static string? ParseBuildType(string output) => output.Trim() switch
    {
        "user" => "User",
        "userdebug" => "UserDebug",
        "eng" => "Engineering",
        _ => null,
    };

    public static string? ParseVerifiedBootState(string output) => output.Trim().ToLowerInvariant() switch
    {
        "green" => "Green",
        "yellow" => "Yellow",
        "orange" => "Orange",
        "red" => "Red",
        _ => null,
    };

    public static bool? ParseSelinux(string output) => output.Trim().ToLowerInvariant() switch
    {
        "enforcing" => true,
        "permissive" or "disabled" => false,
        _ => null,
    };

    private static Dictionary<string, string> ParseColonValues(string output)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':', 2);
            if (parts.Length == 2) result[parts[0].Trim()] = parts[1].Trim();
        }
        return result;
    }

    private static Dictionary<string, long> ParseMemoryValues(string output)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split([':', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                result[parts[0]] = value;
        }
        return result;
    }

    private static long? Value(IReadOnlyDictionary<string, long> values, string key) => values.TryGetValue(key, out var value) ? value : null;
    private static int? Integer(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    private static bool? Boolean(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : null;
    private static double? ParseDouble(IReadOnlyList<string> values, int index) =>
        index < values.Count && double.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static (int? Width, int? Height) ReadDimensions(string output, string prefix)
    {
        var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(candidate => candidate.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (line is null) return (null, null);
        var match = DimensionsRegex().Match(line);
        return match.Success &&
            int.TryParse(match.Groups["width"].Value, out var width) &&
            int.TryParse(match.Groups["height"].Value, out var height)
            ? (width, height) : (null, null);
    }

    private static int? ReadTrailingInteger(string output, string prefix)
    {
        var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(candidate => candidate.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (line is null) return null;
        var value = line[(line.IndexOf(':') + 1)..].Trim();
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static string? MapBatteryStatus(int? value) => value switch
    {
        1 => "Unknown",
        2 => "Charging",
        3 => "Discharging",
        4 => "NotCharging",
        5 => "Full",
        _ => null,
    };

    private static string? MapBatteryHealth(int? value) => value switch
    {
        1 => "Unknown",
        2 => "Good",
        3 => "Overheat",
        4 => "Dead",
        5 => "OverVoltage",
        6 => "UnspecifiedFailure",
        7 => "Cold",
        _ => null,
    };
}
