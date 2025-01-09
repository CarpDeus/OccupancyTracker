using Enyim.Caching;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.IService;
using OccupancyTracker.Models;
using Sqids;

namespace OccupancyTracker.Service
{
    public class AdminUtilsService : IAdminUtilsService
    {
        private readonly IMemcachedClient _memcachedClient;

        public AdminUtilsService(IMemcachedClient memcachedClient)
        {
            _memcachedClient = memcachedClient;
        }

        public void ClearCache()
        {
             _memcachedClient.FlushAll();
        }
    }
}