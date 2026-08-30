using System.Net;
using System.Security.Cryptography;
using ADBMCPSharp.Adb;
using ADBMCPSharp.Configuration;
using ADBMCPSharp.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ADBMCPSharp.Services;

public sealed class PackageAdministrationService(
    DeviceInventory inventory,
    CapabilityPolicy policy,
    IAdbTransport transport,
    DeviceOperationCoordinator coordinator,
    IHttpClientFactory httpClientFactory,
    IOptions<AdbOptions> options,
    ILogger<PackageAdministrationService> logger)
{
    private readonly AdbOptions _options = options.Value;

    public IReadOnlyList<ApkArtifactSummary>? ListInstallableApks(string deviceAlias)
    {
        if (!inventory.TryGet(deviceAlias, out var device)) return null;
        return _options.ApkArtifacts
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ApkArtifactSummary(
                x.Key,
                string.IsNullOrWhiteSpace(x.Value.DisplayName) ? x.Key : x.Value.DisplayName,
                x.Value.AllowReplace,
                policy.PackageInstall(device) && IsAllowedForDevice(x.Value, device.Alias)))
            .ToArray();
    }

    public async Task<PackageOperationResult> InstallAsync(
        string deviceAlias, string artifactAlias, bool confirmPackageChange, CancellationToken cancellationToken)
    {
        if (!inventory.TryGet(deviceAlias, out var device)) return NotFound(deviceAlias, artifactAlias, "Unknown device alias.");
        var pair = _options.ApkArtifacts.FirstOrDefault(x => string.Equals(x.Key, artifactAlias, StringComparison.OrdinalIgnoreCase));
        if (pair.Key is null) return NotFound(device.Alias, artifactAlias, "Unknown APK artifact alias.");
        if (!policy.PackageInstall(device) || !IsAllowedForDevice(pair.Value, device.Alias))
            return Denied(device.Alias, pair.Key, "APK installation is not enabled for this device and artifact.");
        if (!confirmPackageChange)
            return Denied(device.Alias, pair.Key, "Explicit package-change confirmation is required.");

        return await coordinator.WithLockAsync(device.Alias, async token =>
        {
            PreparedApk? prepared = null;
            try
            {
                prepared = await PrepareAsync(pair.Value, token);
                if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(prepared.Sha256), Convert.FromHexString(pair.Value.Sha256)))
                    return Failed(device.Alias, pair.Key, "APK checksum verification failed.");

                var result = await transport.ExecuteAsync(device.Server, device.Device.Selector,
                    new(AdbRequestKind.InstallApk, prepared.Path, pair.Value.AllowReplace, _options.PackageOperationTimeoutSeconds), token);
                Audit(device.Alias, pair.Key, "install", result.Success);
                if (!result.Success) return FromFailure(device.Alias, pair.Key, result);

                var check = await transport.ExecuteAsync(device.Server, device.Device.Selector,
                    new(AdbRequestKind.GetPackagePath, pair.Value.Package), token);
                return check.Success
                    ? new(device.Alias, pair.Key, OperationState.ObservedComplete, "APK installation was observed.", true)
                    : new(device.Alias, pair.Key, OperationState.Accepted, "ADB accepted the APK; installation could not be verified.", false);
            }
            catch (InvalidDataException ex)
            {
                logger.LogWarning("APK preparation rejected for artifact alias {ArtifactAlias}: {Reason}", pair.Key, ex.Message);
                return Failed(device.Alias, pair.Key, ex.Message);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(device.Alias, pair.Key, OperationState.TimedOut, "APK preparation timed out.");
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException)
            {
                logger.LogWarning("APK preparation failed for artifact alias {ArtifactAlias}", pair.Key);
                return Failed(device.Alias, pair.Key, "The configured APK artifact could not be prepared.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("APK preparation timed out for artifact alias {ArtifactAlias}", pair.Key);
                return new(device.Alias, pair.Key, OperationState.TimedOut, "The configured APK download timed out.");
            }
            finally
            {
                if (prepared?.DeleteWhenFinished == true)
                    try { File.Delete(prepared.Path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }, cancellationToken);
    }

    public async Task<PackageOperationResult> UninstallAsync(
        string deviceAlias, string appAlias, bool confirmPackageChange, CancellationToken cancellationToken)
    {
        if (!inventory.TryGet(deviceAlias, out var device)) return NotFound(deviceAlias, appAlias, "Unknown device alias.");
        var pair = device.Device.AllowedApps.FirstOrDefault(x => string.Equals(x.Key, appAlias, StringComparison.OrdinalIgnoreCase));
        if (pair.Key is null) return NotFound(device.Alias, appAlias, "Unknown application alias.");
        if (!policy.PackageUninstall(device) || !pair.Value.AllowUninstall)
            return Denied(device.Alias, pair.Key, "Application uninstall is not enabled for this allowlisted application.");
        if (!confirmPackageChange)
            return Denied(device.Alias, pair.Key, "Explicit package-change confirmation is required.");

        return await coordinator.WithLockAsync(device.Alias, async token =>
        {
            var before = await transport.ExecuteAsync(device.Server, device.Device.Selector,
                new(AdbRequestKind.GetPackagePath, pair.Value.Package), token);
            if (!before.Success && before.FailureKind is AdbFailureKind.Offline or AdbFailureKind.Unauthorized or AdbFailureKind.Unavailable or AdbFailureKind.TimedOut)
                return FromFailure(device.Alias, pair.Key, before);
            if (!before.Success)
                return new(device.Alias, pair.Key, OperationState.ObservedComplete, "Application was already absent.", true);

            var result = await transport.ExecuteAsync(device.Server, device.Device.Selector,
                new(AdbRequestKind.UninstallPackage, pair.Value.Package, TimeoutSeconds: _options.PackageOperationTimeoutSeconds), token);
            Audit(device.Alias, pair.Key, "uninstall", result.Success);
            if (!result.Success) return FromFailure(device.Alias, pair.Key, result);
            var check = await transport.ExecuteAsync(device.Server, device.Device.Selector,
                new(AdbRequestKind.GetPackagePath, pair.Value.Package), token);
            return !check.Success && check.FailureKind is not (AdbFailureKind.Offline or AdbFailureKind.Unauthorized or AdbFailureKind.Unavailable or AdbFailureKind.TimedOut)
                ? new(device.Alias, pair.Key, OperationState.ObservedComplete, "Application removal was observed.", true)
                : new(device.Alias, pair.Key, OperationState.Accepted, "ADB accepted the removal; absence could not be verified.", false);
        }, cancellationToken);
    }

    private async Task<PreparedApk> PrepareAsync(ApkArtifactOptions artifact, CancellationToken token)
    {
        if (Path.IsPathFullyQualified(artifact.Source))
        {
            var file = new FileInfo(artifact.Source);
            if (!file.Exists) throw new InvalidDataException("The configured APK artifact is unavailable.");
            if (file.Length > _options.MaxApkBytes) throw new InvalidDataException("The configured APK exceeds the size limit.");
            await using var stream = file.OpenRead();
            return new(file.FullName, Convert.ToHexString(await SHA256.HashDataAsync(stream, token)), false);
        }

        var destination = Path.Combine(Path.GetTempPath(), $"adbmcp-{Guid.NewGuid():N}.apk");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.ApkDownloadTimeoutSeconds));
        try
        {
            using var response = await httpClientFactory.CreateClient("apk-artifacts").GetAsync(
                artifact.Source, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("The configured APK download did not remain on HTTPS.");
            if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("The configured APK download was not successful.");
            if (response.Content.Headers.ContentLength > _options.MaxApkBytes)
                throw new InvalidDataException("The configured APK exceeds the size limit.");
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, timeout.Token)) > 0)
            {
                total += read;
                if (total > _options.MaxApkBytes) throw new InvalidDataException("The configured APK exceeds the size limit.");
                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }
            return new(destination, Convert.ToHexString(hash.GetHashAndReset()), true);
        }
        catch
        {
            try { File.Delete(destination); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    private static bool IsAllowedForDevice(ApkArtifactOptions artifact, string deviceAlias) =>
        artifact.AllowedDevices.Contains(deviceAlias, StringComparer.OrdinalIgnoreCase);

    private void Audit(string deviceAlias, string itemAlias, string operation, bool accepted) =>
        logger.LogInformation("Package operation {Operation} for device {DeviceAlias} and item {ItemAlias}: {Outcome}",
            operation, deviceAlias, itemAlias, accepted ? "accepted" : "failed");

    private static PackageOperationResult FromFailure(string device, string item, AdbExecutionResult result) =>
        new(device, item, result.FailureKind switch
        {
            AdbFailureKind.TimedOut => OperationState.TimedOut,
            AdbFailureKind.Offline => OperationState.Offline,
            AdbFailureKind.Unauthorized => OperationState.Unauthorized,
            AdbFailureKind.Unavailable => OperationState.Indeterminate,
            _ => OperationState.Failed,
        }, result.Message ?? "Package operation failed.");
    private static PackageOperationResult Failed(string device, string item, string message) => new(device, item, OperationState.Failed, message);
    private static PackageOperationResult Denied(string device, string item, string message) => new(device, item, OperationState.Denied, message);
    private static PackageOperationResult NotFound(string device, string item, string message) => new(device, item, OperationState.NotFound, message);
    private sealed record PreparedApk(string Path, string Sha256, bool DeleteWhenFinished);
}
