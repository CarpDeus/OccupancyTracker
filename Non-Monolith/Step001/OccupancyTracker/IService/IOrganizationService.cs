using Microsoft.EntityFrameworkCore;
using OccupancyTracker.Data.Models;

//using OccupancyTracker.Models.Migrations;

namespace OccupancyTracker.IService
{
    public interface IOrganizationService
    {
        
        /// <summary>
        /// GetAsync a list of organizations that the user has access to based on a filter.
        /// Returns all Active organizations and any deleted organizations if the user is
        /// Organization Admin for the deleted ones
        /// </summary>
        /// <param name="userInformation">IUser making the request</param>
        /// <param name="filter">A filter to use in filtering results, empty string returns all</param>
        /// <returns>List of organizations</returns>
        Task<List<Organization>> GetListAsync(UserInformation userInformation, string filter = "", bool forceCacheRefresh = false);

        

        /// <summary>
        /// GetAsync a list of active organizations that the user has access to based on a filter
        /// </summary>
        /// <param name="userInformation">IUser making the request</param>
        /// <param name="filter">A filter to use in filtering results, empty string returns all</param>
        /// <returns>List of organizations</returns>
        Task<List<Organization>> GetActiveListAsync(UserInformation userInformation, string filter, bool forceCacheRefresh = false);

        /// <summary>
        /// GetAsync a list of deleted organizations that the user has access to based on a filter
        /// </summary>
        /// <param name="userInformation">IUser making the request</param>
        /// <param name="filter">A filter to use in filtering results, empty string returns all</param>
        /// <returns>List of organizations</returns>
        Task<List<Organization>> GetDeletedListAsync(UserInformation userInformation, string filter, bool forceCacheRefresh = false);


        /// <summary>
        /// GetAsync a list of permanently deleted organizations based on a filter
        /// </summary>
        /// <param name="userInformationSqid">Id of the user making the request. Must be SuperAdmin</param>
        /// <param name="filter">A filter to use in filtering results</param>
        /// <returns>List of organizations</returns>
        Task<List<Organization>> GetPermanentlyDeletedListAsync(UserInformation userInformation, string filter, bool forceCacheRefresh = false);

        /// <summary>
        /// GetAsync an individual organization
        /// </summary>
        /// <param name="organizationSqid">Organization Sqid</param>
        /// <param name="userInformation">IUser making the request</param>
        /// <returns>Organization object</returns>
        /// <exception cref="Exception">Throws an exception if the user is not authorized to perform the operation or if the organization is not active</exception>"
        Task<Organization?> GetAsync(string organizationSqid, UserInformation userInformation, bool forceCacheRefresh = false);

        /// <summary>
        /// GetAsync an individual organization by location id
        /// </summary>
        /// <param name="locationSqid">LocationSqid</param>
        /// <param name="userInformation">IUser making the request</param>
        /// <returns>Organization object</returns>
        /// <exception cref="Exception">Throws an exception if the user is not authorized to perform the operation</exception>"
        Task<Organization?> GetByLocationAsync(string locationSqid, UserInformation userInformation, bool forceCacheRefresh = false);

        /// <summary>
        /// SaveAsync an organization's data
        /// </summary>
        /// <param name="entity">Organization object</param>
        /// <param name="userInformation">IUser making the request</param>
        /// <returns>Organization</returns>
        /// <exception cref="Exception">Throws an exception if the user is not authorized to perform the operation</exception>"
        Task<Organization?> SaveAsync(Organization entity, UserInformation userInformation);

        

        /// <summary>
        /// Validates unique organization name across data
        /// </summary>
        /// <param name="organizationName"></param>
        /// <param name="organizationSqid"></param>
        /// <returns></returns>
        Task<bool> IsUniqueNameAsync(string organizationName, string organizationSqid);

        /// <summary>
        /// Change the status of an organization from active to suspenede to deleted and back
        /// </summary>
        /// <param name="organizationSqid"></param>
        /// <param name="fromStatus"></param>
        /// <param name="toStatus"></param>
        /// <param name="userInformationSqid"></param>
        /// <param name="forceCacheRefresh"></param>
        /// <returns>The updated organization</returns>
        /// <exception cref="InvalidOperationException">Any errors. Must be an organization admin to call and must be a Super Admin to change from PermanentlyDeleted</exception>
        Task<Organization?> ChangeStatusAsync(string organizationSqid, int fromStatus, int toStatus, UserInformation userInformation);
    }
}
