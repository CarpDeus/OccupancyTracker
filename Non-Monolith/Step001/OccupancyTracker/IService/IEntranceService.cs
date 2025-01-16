using OccupancyTracker.Data.Models;

namespace OccupancyTracker.IService
{
    public interface IEntranceService
    {
        /// <summary>
        /// GetAsync a list of organizations that the user has access to based on a filter.
        /// Returns all Active organizations and any deleted organizations if the user is
        /// Organization Admin for the deleted ones
        /// </summary>
        /// <param name="userInformation">IUser making the request</param>
        /// <param name="filter">A filter to use in filtering results, empty string returns all</param>
        /// <returns>List of organizations</returns>
        Task<List<Entrance>> GetListAsync(UserInformation userInformation, string organizationSqid, string locationSqid, string filter = "", bool forceCacheRefresh = false);



        /// <summary>
        /// GetAsync a list of active organizations that the user has access to based on a filter
        /// </summary>
        /// <param name="userInformation">IUser making the request</param>
        /// <param name="filter">A filter to use in filtering results, empty string returns all</param>
        /// <returns>List of organizations</returns>
        Task<List<Entrance>> GetActiveListAsync(UserInformation userInformation, string organizationSqid, string locationSqid, string filter, bool forceCacheRefresh = false);

        /// <summary>
        /// GetAsync a list of deleted organizations that the user has access to based on a filter
        /// </summary>
        /// <param name="userInformation">IUser making the request</param>
        /// <param name="filter">A filter to use in filtering results, empty string returns all</param>
        /// <returns>List of organizations</returns>
        Task<List<Entrance>> GetDeletedListAsync(UserInformation userInformation, string organizationSqid, string locationSqid, string filter, bool forceCacheRefresh = false);


        /// <summary>
        /// GetAsync a list of permanently deleted organizations based on a filter
        /// </summary>
        /// <param name="userInformationSqid">Id of the user making the request. Must be SuperAdmin</param>
        /// <param name="filter">A filter to use in filtering results</param>
        /// <returns>List of organizations</returns>
        Task<List<Entrance>> GetPermanentlyDeletedListAsync(UserInformation userInformation, string organizationSqid, string locationSqid, string filter, bool forceCacheRefresh = false);

        /// <summary>
        /// GetAsync an individual organization
        /// </summary>
        /// <param name="organizationSqid">Organization Sqid</param>
        /// <param name="userInformation">IUser making the request</param>
        /// <returns>Organization object</returns>
        /// <exception cref="Exception">Throws an exception if the user is not authorized to perform the operation or if the organization is not active</exception>"
        Task<Entrance?> GetAsync(string organizationSqid, string locationSqid, string entranceSqid ,UserInformation userInformation, bool forceCacheRefresh = false);


        /// <summary>
        /// SaveAsync an organization's data
        /// </summary>
        /// <param name="entrance">Entrance object</param>
        /// <param name="organizationSqid">Organization Sqid</param>
        /// <param name="userInformation">IUser making the request</param>
        /// <returns>Organization</returns>
        /// <exception cref="Exception">Throws an exception if the user is not authorized to perform the operation</exception>"
        Task<Entrance> SaveAsync(Entrance entrance, string organizationSqid, string locationSqid, UserInformation userInformation);



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
        Task<Entrance?> ChangeStatusAsync(string organizationSqid, string locationSqid, string entranceSqid, int fromStatus, int toStatus, UserInformation userInformation);


    }
}
