using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public sealed class ServiceProbeCoordinator
{
    private static readonly TimeSpan DefaultCacheWindow = TimeSpan.FromSeconds(2);

    private readonly Func<CancellationToken, Task<ServiceProbeResult>> _probe;
    private readonly TimeSpan _cacheWindow;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ServiceProbeResult? _cachedResult;
    private DateTimeOffset _cachedAt;

    public ServiceProbeCoordinator(
        Func<CancellationToken, Task<ServiceProbeResult>> probe,
        TimeSpan? cacheWindow = null)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _cacheWindow = cacheWindow ?? DefaultCacheWindow;
        if (_cacheWindow < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(cacheWindow));
    }

    public async Task<ServiceProbeResult> ProbeAsync(
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        if (!forceRefresh && TryGetCached(out var cached))
            return cached;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && TryGetCached(out cached))
                return cached;

            var result = await _probe(cancellationToken);
            _cachedResult = result;
            _cachedAt = DateTimeOffset.UtcNow;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryGetCached(out ServiceProbeResult result)
    {
        if (_cachedResult is not null
            && DateTimeOffset.UtcNow - _cachedAt <= _cacheWindow)
        {
            result = _cachedResult;
            return true;
        }

        result = null!;
        return false;
    }
}
