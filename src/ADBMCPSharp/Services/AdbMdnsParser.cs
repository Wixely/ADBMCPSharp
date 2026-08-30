using System.Text.RegularExpressions;

namespace ADBMCPSharp.Services;

public static partial class AdbMdnsParser
{
    [GeneratedRegex(
        @"^(?<instance>.+?)\s+(?<type>_(?:adb|adb-tls-connect|adb-tls-pairing)\._tcp\.?)\s+(?<endpoint>\S+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex ServiceLineRegex();

    internal static IReadOnlyList<AdbMdnsCandidate> Parse(string output, int maximumResults)
    {
        var candidates = new List<AdbMdnsCandidate>(Math.Min(maximumResults, 16));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = ServiceLineRegex().Match(line);
            if (!match.Success) continue;

            var identity = string.Concat(
                match.Groups["instance"].Value, "\n",
                match.Groups["type"].Value, "\n",
                match.Groups["endpoint"].Value);
            if (!seen.Add(identity)) continue;

            candidates.Add(new(NormalizeServiceType(match.Groups["type"].Value)));
            if (candidates.Count == maximumResults) break;
        }

        return candidates;
    }

    private static string NormalizeServiceType(string serviceType) => serviceType.TrimEnd('.').ToLowerInvariant() switch
    {
        "_adb._tcp" => "LegacyTcpAdb",
        "_adb-tls-connect._tcp" => "WirelessDebugging",
        "_adb-tls-pairing._tcp" => "Pairing",
        _ => throw new InvalidOperationException("Unexpected ADB mDNS service type."),
    };
}

internal sealed record AdbMdnsCandidate(string ServiceType);
