using OccupancyTracker.DTO;
using OccupancyTracker.Models;
using OccupancyTracker.Service;

namespace OccupancyTracker.IService
{
    /// <summary>
    /// Interface for the EntranceCounterService
    /// </summary>
    public interface IEntranceCounterService
    {
        /// <summary>
        /// Get the entrance counter for the specified Id
        /// </summary>
        /// <param name="entranceCounterSqid">Identifier for the entrance counter</param>
        /// <param name="forceCacheRefresh">When true, get from data store instead of cache</param>
        /// <returns>Requested EntranceCounter</returns>
        Task<EntranceCounter>  GetAsync(string entranceCounterSqid, bool forceCacheRefresh = false);

        /// <summary>
        /// Get the entrance counter data transfer object for the specified Id
        /// </summary>
        /// <param name="entranceCounterSqid">Identifier for the entrance counter</param>
        /// <param name="forceCacheRefresh">When true, get from data store instead of cache</param>
        /// <returns>Requested EntranceCounterDTO if found</returns>
        Task<EntranceCounterDto?> GetCounterForTrackerAsync(string entranceCounterSqid, bool forceCacheRefresh = false);

        /// <summary>
        /// Update the count for the entrance counter
        /// </summary>
        /// <param name="entranceCounterSqid">Identifier for the entrance counter</param>
        /// <param name="count">Amount to change. Positive values indicate people entering location, negative values indicate people leaving</param>
        /// <param name="forceCacheRefresh">When true, get from data store instead of cache</param>
        /// <returns>Current occupancy for the location after update</returns>
        Task<int> UpdateCountAsync(string entranceCounterSqid, int count, bool forceCacheRefresh = false);

        /// <summary>
        /// Generate a new entrance counter for the specified entrance
        /// </summary>
        /// <param name="entrance">The entrance linked to the new counter</param>
        /// <returns>The newly generated EntranceCounter</returns>
        Task<EntranceCounter>  ReplaceAsync(Entrance entrance);

        /// <summary>
        /// Get the current occupancy for the location associated with the counter
        /// </summary>
        /// <param name="entranceCounterSqid">Identifier for the entrance counter</param>
        /// <param name="forceCacheRefresh">When true, get from data store instead of cache</param>
        /// <returns>Current occupancy for the location</returns>
        Task<int> GetCountAsync(string entranceCounterSqid, bool forceCacheRefresh = false);

        /// <summary>
        /// Get the location sqid for the entrance counter
        /// </summary>
        /// <param name="entranceCounterSqid">Identifier for the entrance counter</param>
        /// <returns>The location Sqid</returns>
        Task<string> GetLocationSqidAsync(string entranceCounterSqid);

        /// <summary>
        /// Create a new entrance counter for the specified entrance. 
        /// </summary>
        /// <param name="userInformationSqid"></param>
        /// <param name="entrance"></param>
        /// <returns>Entrance Counter</returns>
        Task<string> CreateEntranceCounterForEntranceAsync(string userInformationSqid, Entrance entrance);
    }
}
