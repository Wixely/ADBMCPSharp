using Microsoft.Extensions.Options;

namespace ADBMCPSharp.Configuration;

public sealed class AdbOptionsValidator : IValidateOptions<AdbOptions>
{
    public ValidateOptionsResult Validate(string? name, AdbOptions options)
    {
        var failures = new List<string>();
        if (options.Servers.Count > 25) failures.Add("At most 25 configured ADB servers are supported.");
        if (options.Devices.Count > 100) failures.Add("At most 100 configured devices are supported.");
        if (options.ApkArtifacts.Count > 100) failures.Add("At most 100 configured APK artifacts are supported.");

        foreach (var (alias, server) in options.Servers)
        {
            if (!IsAlias(alias)) failures.Add($"ADB server alias '{alias}' is invalid.");
            if (server.Mode == AdbServerMode.Remote && string.IsNullOrWhiteSpace(server.Host))
                failures.Add($"Remote ADB server '{alias}' requires a host.");
            if (server.Host is { } host && (host.Length > 253 || host.Any(char.IsControl) || host.Any(char.IsWhiteSpace)))
                failures.Add($"ADB server '{alias}' has an invalid host.");
        }

        foreach (var (alias, device) in options.Devices)
        {
            if (!IsAlias(alias)) failures.Add($"Device alias '{alias}' is invalid.");
            if (!options.Servers.Keys.Any(serverAlias => string.Equals(serverAlias, device.Server, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"Device '{alias}' references an unknown server.");
            if (!IsSelector(device.Selector))
                failures.Add($"Device '{alias}' requires a printable, whitespace-free selector no longer than 200 characters that does not start with '-'.");
            if (device.AllowedApps.Count > 100) failures.Add($"Device '{alias}' has more than 100 allowed apps.");
            foreach (var (appAlias, app) in device.AllowedApps)
            {
                if (!IsAlias(appAlias)) failures.Add($"App alias '{appAlias}' on '{alias}' is invalid.");
                if (!IsPackage(app.Package)) failures.Add($"App '{appAlias}' on '{alias}' has an invalid package name.");
            }
        }

        foreach (var (alias, artifact) in options.ApkArtifacts)
        {
            if (!IsAlias(alias)) failures.Add($"APK artifact alias '{alias}' is invalid.");
            if (!IsPackage(artifact.Package)) failures.Add($"APK artifact '{alias}' has an invalid package name.");
            if (!IsArtifactSource(artifact.Source))
                failures.Add($"APK artifact '{alias}' requires an absolute local path or credential-free HTTPS URL without query or fragment.");
            if (artifact.Sha256.Length != 64 || !artifact.Sha256.All(char.IsAsciiHexDigit))
                failures.Add($"APK artifact '{alias}' requires a 64-character SHA-256 value.");
            if (artifact.AllowedDevices.Count == 0)
                failures.Add($"APK artifact '{alias}' requires at least one allowed device alias.");
            foreach (var deviceAlias in artifact.AllowedDevices)
            {
                if (!options.Devices.Keys.Any(candidate => string.Equals(candidate, deviceAlias, StringComparison.OrdinalIgnoreCase)))
                    failures.Add($"APK artifact '{alias}' references unknown device alias '{deviceAlias}'.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    internal static bool IsAlias(string value) =>
        value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool IsSelector(string value) =>
        value.Length is > 0 and <= 200 && value[0] != '-' && value.All(c => c is >= '!' and <= '~');

    internal static bool IsPackage(string value) =>
        value.Length is > 0 and <= 255 && value.Contains('.') &&
        value.Split('.').All(segment => segment.Length > 0 &&
            (char.IsAsciiLetter(segment[0]) || segment[0] == '_') &&
            segment.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'));

    private static bool IsArtifactSource(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || value.Any(char.IsControl)) return false;
        if (Path.IsPathFullyQualified(value)) return value.EndsWith(".apk", StringComparison.OrdinalIgnoreCase);
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            string.IsNullOrEmpty(uri.Query) &&
            string.IsNullOrEmpty(uri.Fragment) &&
            uri.AbsolutePath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ServerOptionsValidator : IValidateOptions<ServerOptions>
{
    public ValidateOptionsResult Validate(string? name, ServerOptions options)
    {
        var isLoopback = string.Equals(options.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            System.Net.IPAddress.TryParse(options.Host, out var ip) && System.Net.IPAddress.IsLoopback(ip);
        if (!isLoopback && string.IsNullOrWhiteSpace(options.ApiKey))
            return ValidateOptionsResult.Fail("Server:ApiKey is required when binding beyond loopback.");
        if (options.ApiKey is { Length: > 0 and < 24 })
            return ValidateOptionsResult.Fail("Server:ApiKey must contain at least 24 characters.");
        return ValidateOptionsResult.Success;
    }
}
