using Microsoft.EntityFrameworkCore;
//using OccupancyTracker.Models.Migrations;
using OccupancyTracker.Models;

namespace OccupancyTracker.IService
{
    public interface ILocationService
    {


        /// <summary>
        /// GetAsync a list of organizations that the user has access to based on a filter.
        /// Returns all Active organizations and any deleted organizations if the user is
        /// Organization Admin for the deleted ones
        /// </summary>
        /// <param name="userInformation">IUser making the request</param>
        /// <param name="filter">A filter to use in filtering results, empty string returns all</param>
        /// <returns>List of organizations</returns>
        Task<List<Location>> GetListAsync(UserInformation userInformation, string organizationSqid, string filter = "", bool forceCacheRefresh = false);

        /// <summary>
        /// GetAsync a list of organizations, includes all States but must be SuperAdmin to use
        /// </summary>
        /// <param name="userInformation">IUser making the request</param>
        /// <param name="filter">A filter to use in filtering results, empty string returns all</param>
        /// <returns>List of organizations</returns>
        Task<List<Location>> GetFullListAsync(UserInformation userInformation, string organizationSqid, string filter = "", bool forceCacheRefresh = false);


        /// <summary>
        /// GetAsync a list of active organizations that the user has access to based on a filter
        /// </summary>
        /// <param name="userInformation">IUser making the request</param>
        /// <param name="filter">A filter to use in filtering results, empty string returns all</param>
        /// <returns>List of organizations</returns>
        Task<List<Location>> GetActiveListAsync(UserInformation userInformation, string organizationSqid, string filter, bool forceCacheRefresh = false);

        /// <summary>
        /// GetAsync a list of deleted organizations that the user has access to based on a filter
        /// </summary>
        /// <param name="userInformation">IUser making the request</param>
        /// <param name="filter">A filter to use in filtering results, empty string returns all</param>
        /// <returns>List of organizations</returns>
        Task<List<Location>> GetDeletedListAsync(UserInformation userInformation, string organizationSqid, string filter, bool forceCacheRefresh = false);


        /// <summary>
        /// GetAsync a list of permanently deleted organizations based on a filter
        /// </summary>
        /// <param name="userInformationSqid">Id of the user making the request. Must be SuperAdmin</param>
        /// <param name="filter">A filter to use in filtering results</param>
        /// <returns>List of organizations</returns>
        Task<List<Location>> GetPermanentlyDeletedListAsync(UserInformation userInformation, string organizationSqid, string filter, bool forceCacheRefresh = false);

        /// <summary>
        /// GetAsync an individual organization
        /// </summary>
        /// <param name="organizationSqid">Organization Sqid</param>
        /// <param name="userInformation">IUser making the request</param>
        /// <returns>Organization object</returns>
        /// <exception cref="Exception">Throws an exception if the user is not authorized to perform the operation or if the organization is not active</exception>"
        Task<Location?> GetAsync(string organizationSqid, string locationSqid, UserInformation userInformation, bool forceCacheRefresh = false);


        /// <summary>
        /// SaveAsync an organization's data
        /// </summary>
        /// <param name="location">Location object</param>
        /// <param name="organizationSqid">Organization Sqid</param>
        /// <param name="userInformation">IUser making the request</param>
        /// <returns>Organization</returns>
        /// <exception cref="Exception">Throws an exception if the user is not authorized to perform the operation</exception>"
        Task<Location> SaveAsync(Location location, string organizationSqid, UserInformation userInformation);



        /// <summary>
        /// Change the status of an organization from active to suspenede to deleted and back
        /// </summary>
        /// <param name="organizationSqid"></param>
        /// <param name="locationSqid"></param>
        /// <param name="fromStatus"></param>
        /// <param name="toStatus"></param>
        /// <param name="userInformationSqid"></param>
        /// <param name="forceCacheRefresh"></param>
        /// <returns>The updated organization</returns>
        /// <exception cref="InvalidOperationException">Any errors. Must be an organization admin to call and must be a Super Admin to change from PermanentlyDeleted</exception>
        Task<Location?> ChangeStatusAsync(string organizationSqid, string locationSqid, int fromStatus, int toStatus, UserInformation userInformation);


        /// <summary>
        /// Set the current occupancy value for a location
        /// </summary>
        /// <param name="organizationSqid"></param>
        /// <param name="locationSqid"></param>
        /// <param name="currentOccupancy"></param>
        /// <param name="userInformation"></param>
        /// <returns></returns>
        Task<int> SetLocationCurrentOccupancy(string organizationSqid, string locationSqid, int currentOccupancy,UserInformation userInformation);

        Task<LocationOccupancyStats> GetLocationOccupancyStats(string locationSqid);
    }
}

