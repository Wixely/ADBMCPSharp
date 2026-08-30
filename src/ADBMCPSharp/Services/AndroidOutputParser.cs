using System.Text.RegularExpressions;

namespace ADBMCPSharp.Services;

public static partial class AndroidOutputParser
{
    [GeneratedRegex(@"(?:mCurrentFocus|mFocusedApp).*?\s(?:u\d+\s+)?(?<package>[A-Za-z][A-Za-z0-9_.]*)/", RegexOptions.IgnoreCase)]
    private static partial Regex ForegroundRegex();

    public static (bool? Awake, bool? DisplayOn) ParsePower(string output)
    {
        bool? awake = null;
        bool? displayOn = null;

        if (ContainsAny(output, "mWakefulness=Awake", "Wakefulness: Awake", "mInteractive=true")) awake = true;
        else if (ContainsAny(output, "mWakefulness=Asleep", "mWakefulness=Dozing", "mInteractive=false")) awake = false;

        if (ContainsAny(output, "Display Power: state=ON", "mScreenOn=true", "mHoldingDisplaySuspendBlocker=true")) displayOn = true;
        else if (ContainsAny(output, "Display Power: state=OFF", "mScreenOn=false", "mHoldingDisplaySuspendBlocker=false")) displayOn = false;

        return (awake, displayOn);
    }

    public static string? ParseForegroundPackage(string output)
    {
        var match = ForegroundRegex().Match(output);
        return match.Success ? match.Groups["package"].Value : null;
    }

    public static bool? ParseDreaming(string output)
    {
        if (ContainsAny(output, "isDreaming=true", "mDreaming=true")) return true;
        if (Regex.IsMatch(output, @"mCurrentDream(?:Name)?\s*[:=]\s*(?!null(?:\s|$))\S+", RegexOptions.IgnoreCase)) return true;
        if (ContainsAny(output, "isDreaming=false", "mDreaming=false", "mCurrentDream=null",
            "mCurrentDream: null", "mCurrentDreamName=null")) return false;
        return null;
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
}
