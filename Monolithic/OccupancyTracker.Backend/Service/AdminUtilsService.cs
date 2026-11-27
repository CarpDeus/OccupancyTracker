using OccupancyTracker.IRepository;
using OccupancyTracker.IService;

namespace OccupancyTracker.Service
{
    /// <summary>
    /// Service for admin utility functions.
    /// </summary>
    public class AdminUtilsService : IAdminUtilsService
    {
        private readonly ICacheRepository _cache;

        /// <summary>
        /// Initializes a new instance of the <see cref="AdminUtilsService"/> class.
        /// </summary>
        /// <param name="memcachedClient">The Memcached client.</param>
        public AdminUtilsService(ICacheRepository cache)
        {
            _cache = cache;
        }

        /// <summary>
        /// Requests a cache flush on the available servers.
        /// </summary>
        public Task ClearCacheAsync() => _cache.ClearAsync();
    }
}