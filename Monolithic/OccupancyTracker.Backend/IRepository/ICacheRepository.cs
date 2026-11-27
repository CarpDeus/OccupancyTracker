namespace OccupancyTracker.IRepository;

public interface ICacheRepository
{
    Task WriteAsync<T>(string key, T value, TimeSpan expiration);
    Task DeleteAsync(string key);
    Task ClearAsync();
}
