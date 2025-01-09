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

        Task<bool> HasAccessToOrganizationAsync(string userInformationSqid, string organizationSqid);

        Task<bool> HasAccessToLocationAsync(string userInformationSqid, string locationSqid);

        
        Task<List<CurrentUserRoleInformation>> GetUserRolesFilteredAsync(string userInformationSqid, string organizationSqid = "", string locationSqid = "");
        Task<string> GetUserInformationRoleForOrganizationAsync(string userInformationSqid, string orgSqid);
        Task<string> GetUserInformationRoleForLocationAsync(string userInformationSqid, string orgSqid, string locationSqid, bool forceRefreshCache=false);

        Task<bool> IsLocAdminAsync(string userInformationSqid, string orgSqid, string locationSqid, bool forceCacheRefresh = false);
        Task<bool> IsOrgAdminAsync(string userInformationSqid, string orgSqid, bool forceCacheRefresh = false);

        public Task<string> LogAccessExceptionAsync(string userInformationSqid, string organizationSqid, string locationSqid, string entranceSqid, string ipAddress, string detailedMessage, string userMessage);

        

    }
}
