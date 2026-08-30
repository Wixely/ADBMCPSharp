using ADBMCPSharp.Configuration;
using Microsoft.Extensions.Options;

namespace ADBMCPSharp.Services;

public sealed class DeviceInventory(IOptions<AdbOptions> options)
{
    private readonly AdbOptions _options = options.Value;

    public IReadOnlyList<string> Aliases => _options.Devices.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    public IReadOnlyList<string> ServerAliases => _options.Servers.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public bool TryGetServer(string alias, out ConfiguredServer configured)
    {
        var pair = _options.Servers.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, alias, StringComparison.OrdinalIgnoreCase));
        if (pair.Key is null)
        {
            configured = default!;
            return false;
        }

        configured = new(pair.Key, pair.Value);
        return true;
    }

    public bool TryGet(string alias, out ConfiguredDevice configured)
    {
        var devicePair = _options.Devices.FirstOrDefault(pair =>
            string.Equals(pair.Key, alias, StringComparison.OrdinalIgnoreCase));
        var serverPair = _options.Servers.FirstOrDefault(pair =>
            string.Equals(pair.Key, devicePair.Value?.Server, StringComparison.OrdinalIgnoreCase));
        if (devicePair.Key is null || serverPair.Key is null)
        {
            configured = default!;
            return false;
        }

        configured = new(devicePair.Key, devicePair.Value, serverPair.Value);
        return true;
    }
}

public sealed record ConfiguredDevice(string Alias, AdbDeviceOptions Device, AdbServerOptions Server);
public sealed record ConfiguredServer(string Alias, AdbServerOptions Server);
