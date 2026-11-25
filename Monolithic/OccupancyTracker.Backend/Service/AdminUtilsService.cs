using Enyim.Caching;
using OccupancyTracker.IService;

namespace OccupancyTracker.Service
{
    /// <summary>
    /// Service for admin utility functions.
    /// </summary>
    public class AdminUtilsService : IAdminUtilsService
    {
        private readonly IMemcachedClient _memcachedClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="AdminUtilsService"/> class.
        /// </summary>
        /// <param name="memcachedClient">The Memcached client.</param>
        public AdminUtilsService(IMemcachedClient memcachedClient)
        {
            _memcachedClient = memcachedClient;
        }

        /// <summary>
        /// Requests a cache flush on the available servers.
        /// </summary>
        public Task ClearCacheAsync() => _memcachedClient.FlushAllAsync();
    }
}