using OccupancyTracker.DTO;
using OccupancyTracker.Models;
using OccupancyTracker.Service;

namespace OccupancyTracker.IService
{
    public interface IEntranceCounterService
    {
        Task<EntranceCounter>  GetAsync(string sqid, bool forceCacheRefresh = false);
        Task<EntranceCounterDto?> GetCounterForTrackerAsync(string sqid, bool forceCacheRefresh = false);
        Task<int> UpdateCountAsync(string sqid, int count, bool forceCacheRefresh = false);
        Task<EntranceCounter>  ReplaceAsync(Entrance entrance);
        Task<int> GetCountAsync(string sqid, bool forceCacheRefresh = false);
        Task<string> GetLocationSqidAsync(string sqid);
        Task<string> CreateEntranceCounterForEntranceAsync(string userInformationSqid, Entrance entrance);
    }
}
