using Enyim.Caching;
using OccupancyTracker.IRepository;

namespace OccupancyTracker.Repository;

public sealed class CacheRepository(IMemcachedClient client) : ICacheRepository
{
    public Task WriteAsync<T>(string key, T value, TimeSpan expiration) =>
        client.SetAsync(key, value, expiration);
    public Task DeleteAsync(string key) => client.RemoveAsync(key);
    public Task ClearAsync() => client.FlushAllAsync();
}
