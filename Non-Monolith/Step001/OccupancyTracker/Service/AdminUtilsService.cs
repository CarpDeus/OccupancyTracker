using Enyim.Caching;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.IService;
using OccupancyTracker.Models;
using Sqids;

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
        /// Clears the cache.
        /// </summary>
        public void ClearCache()
        {
            _memcachedClient.FlushAll();
        }
    }
}