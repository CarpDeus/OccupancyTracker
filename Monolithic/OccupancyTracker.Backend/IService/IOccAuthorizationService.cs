using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using OccupancyTracker.Models;

namespace OccupancyTracker.IService
{
    public interface IOccAuthorizationService
    {
        
        /// <summary>
        /// GetAsync UserInformation by the SQID
        /// </summary>
        /// <param name="userInformationSqid">The publicly used identifier for the user</param>
        /// <returns>UserInformation object</returns>
        Task<UserInformation> GetAsync(string userInformationSqid);

        /// <summary>
        /// Update UserInformation object
        /// </summary>
        /// <param name="userInformation">The data to be updated</param>
        /// <param name="updateUserInformationSqid">The UserInformationSqid of the user performing the updated. It must either match the user being updated or have SuperAdmin rights</param>
        /// <returns>UserInformation object</returns>
        Task<UserInformation> SaveUserAsync(UserInformation userInformation, string updateUserInformationSqid);

        /// <summary>
        /// Determine if the user has completed registration
        /// </summary>
        /// <param name="userInformation">UserInformation object being evaluated</param>
        /// <returns></returns>
        bool HasCompletedRegistration(UserInformation userInformation);

        /// <summary>
        /// GetAsync UserInformation from the AuthenticationState
        /// </summary>
        /// <param name="state">AuthenticationState object</param>
        /// <returns>UserInformation</returns>
        Task<UserInformation?> GetFromStateAsync(AuthenticationState state);

        /// <summary>
        /// Determine if the user has access to the organization
        /// </summary>
        /// <param name="userInformationSqid"></param>
        /// <param name="organizationSqid"></param>
        /// <returns></returns>
        Task<bool> HasAccessToOrganizationAsync(string userInformationSqid, string organizationSqid);

        /// <summary>
        /// Determine if the user has access to the location
        /// </summary>
        /// <param name="userInformationSqid"></param>
        /// <param name="locationSqid"></param>
        /// <returns></returns>
        Task<bool> HasAccessToLocationAsync(string userInformationSqid, string locationSqid);

        /// <summary>
        /// Get a list of the user roles 
        /// </summary>
        /// <param name="userInformationSqid"></param>
        /// <param name="organizationSqid"></param>
        /// <param name="locationSqid"></param>
        /// <returns></returns>
        Task<List<CurrentUserRoleInformation>> GetUserRolesFilteredAsync(string userInformationSqid, string organizationSqid = "", string locationSqid = "");

        /// <summary>
        /// Get the most permissive role for a user for an organization
        /// </summary>
        /// <param name="userInformationSqid"></param>
        /// <param name="orgSqid"></param>
        /// <returns></returns>
        Task<string> GetUserInformationRoleForOrganizationAsync(string userInformationSqid, string orgSqid);

        /// <summary>
        /// Get the most permissive role for a user for a location
        /// </summary>
        /// <param name="userInformationSqid"></param>
        /// <param name="orgSqid"></param>
        /// <param name="locationSqid"></param>
        /// <param name="forceRefreshCache"></param>
        /// <returns></returns>
        Task<string> GetUserInformationRoleForLocationAsync(string userInformationSqid, string orgSqid, string locationSqid, bool forceRefreshCache=false);

        /// <summary>
        /// Determine if the user is a Location admin
        /// </summary>
        /// <param name="userInformationSqid"></param>
        /// <param name="orgSqid"></param>
        /// <param name="locationSqid"></param>
        /// <param name="forceCacheRefresh"></param>
        /// <returns></returns>
        Task<bool> IsLocAdminAsync(string userInformationSqid, string orgSqid, string locationSqid, bool forceCacheRefresh = false);

        /// <summary>
        /// Determine if the user is an Organization admin
        /// </summary>
        /// <param name="userInformationSqid"></param>
        /// <param name="orgSqid"></param>
        /// <param name="forceCacheRefresh"></param>
        /// <returns></returns>
        Task<bool> IsOrgAdminAsync(string userInformationSqid, string orgSqid, bool forceCacheRefresh = false);

        /// <summary>
        /// Log any access attempt that was denied
        /// </summary>
        /// <param name="userInformationSqid"></param>
        /// <param name="organizationSqid"></param>
        /// <param name="locationSqid"></param>
        /// <param name="entranceSqid"></param>
        /// <param name="ipAddress"></param>
        /// <param name="detailedMessage"></param>
        /// <param name="userMessage"></param>
        /// <returns></returns>
        public Task<string> LogAccessExceptionAsync(string userInformationSqid, string organizationSqid, string locationSqid, string entranceSqid, string ipAddress, string detailedMessage, string userMessage);

        

    }
}
