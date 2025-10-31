using Microsoft.Extensions.Caching.Memory;

namespace MsFundamentals.Trainer.Services;

public sealed class CacheService
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _defaultTtl;

    public CacheService(IMemoryCache cache, IConfiguration cfg)
    {
        _cache = cache;
        var mins = int.TryParse(cfg["Cache:DefaultTtlMinutes"], out var m) ? m : 360;
        _defaultTtl = TimeSpan.FromMinutes(mins);
    }

    public T GetOrSet<T>(string key, Func<T> factory)
    {
        if (_cache.TryGetValue(key, out T? value))
            return value!;
        value = factory();
        _cache.Set(key, value, _defaultTtl);
        return value;
    }

    public void Set<T>(string key, T value, TimeSpan? ttl = null)
    {
        _cache.Set(key, value, ttl ?? _defaultTtl);
    }

    public bool TryGet<T>(string key, out T? value)
    {
        if (_cache.TryGetValue(key, out var obj) && obj is T cast)
        {
            value = cast;
            return true;
        }
        value = default;
        return false;
    }
}
