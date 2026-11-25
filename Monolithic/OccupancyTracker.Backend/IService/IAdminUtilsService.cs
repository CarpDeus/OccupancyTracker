
namespace OccupancyTracker.IService
{
    /// <summary>
    /// Interface for super admin functions
    /// </summary>
    public interface IAdminUtilsService
    {
        /// <summary>
        /// Request a flush on the cache servers available to the client.
        /// </summary>
        Task ClearCacheAsync();
    }
}
