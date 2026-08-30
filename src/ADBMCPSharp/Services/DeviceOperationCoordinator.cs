using System.Collections.Concurrent;

namespace ADBMCPSharp.Services;

public sealed class DeviceOperationCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _deviceLocks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<T> WithLockAsync<T>(string deviceAlias, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        var gate = _deviceLocks.GetOrAdd(deviceAlias, _ => new(1, 1));
        await gate.WaitAsync(cancellationToken);
        try { return await action(cancellationToken); }
        finally { gate.Release(); }
    }
}
