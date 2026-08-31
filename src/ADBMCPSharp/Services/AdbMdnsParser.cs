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
        if (maximumResults <= 0) return [];

        var candidates = new List<AdbMdnsCandidate>(Math.Min(maximumResults, 16));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = ServiceLineRegex().Match(line);
            if (!match.Success) continue;
            var endpoint = match.Groups["endpoint"].Value;
            if (!HasValidEndpoint(endpoint)) continue;
            var serviceType = NormalizeServiceType(match.Groups["type"].Value);

            var identity = string.Concat(
                match.Groups["instance"].Value, "\n",
                serviceType, "\n",
                endpoint);
            if (!seen.Add(identity)) continue;

            candidates.Add(new(serviceType));
            if (candidates.Count == maximumResults) break;
        }

        return candidates;
    }

    private static bool HasValidEndpoint(string endpoint)
    {
        string host;
        string portText;
        if (endpoint.StartsWith("[", StringComparison.Ordinal))
        {
            var delimiter = endpoint.LastIndexOf("]:", StringComparison.Ordinal);
            if (delimiter <= 1) return false;
            host = endpoint[1..delimiter];
            portText = endpoint[(delimiter + 2)..];
        }
        else
        {
            var delimiter = endpoint.LastIndexOf(':');
            if (delimiter <= 0 || endpoint.AsSpan(0, delimiter).Contains(':')) return false;
            host = endpoint[..delimiter];
            portText = endpoint[(delimiter + 1)..];
        }

        return !string.IsNullOrWhiteSpace(host) &&
               ushort.TryParse(portText, out var port) && port > 0;
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
