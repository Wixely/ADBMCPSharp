namespace ADBMCPSharp.Services;

internal static class InstalledAppParser
{
    internal static IReadOnlyList<string> Parse(string output, int maximumResults)
    {
        var packages = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            const string prefix = "package:";
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var packageName = line[prefix.Length..].Trim();
            if (!IsValidPackageName(packageName)) continue;
            packages.Add(packageName);
            if (packages.Count == maximumResults) break;
        }
        return packages.ToArray();
    }

    private static bool IsValidPackageName(string value) =>
        value.Length is > 0 and <= 255 && value.Contains('.') &&
        value.Split('.').All(segment => segment.Length > 0 &&
            (char.IsAsciiLetter(segment[0]) || segment[0] == '_') &&
            segment.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'));
}
