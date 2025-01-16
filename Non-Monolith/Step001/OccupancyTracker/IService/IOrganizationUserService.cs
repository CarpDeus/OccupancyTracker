using Microsoft.EntityFrameworkCore;
using OccupancyTracker.Data.Models;
using OccupancyTracker.DTO;

namespace OccupancyTracker.IService
{
    public interface IOrganizationUserService
    {
        /// <summary>
        /// Get a list of users for an organization
        /// </summary>
        /// <param name="userInformationSqid"></param>
        /// <param name="ipAddress"></param>
        /// <param name="organizationSqid"></param>
        /// <param name="forceCacheRefresh"></param>
        /// <returns></returns>
        Task<List<OrganizationUser>> GetUserListForOrganizationAsync(string userInformationSqid, string ipAddress, string organizationSqid, bool forceCacheRefresh = false);

        /// <summary>
        /// Invite a user to join an organization
        /// </summary>
        /// <param name="userInformationSqid"></param>
        /// <param name="ipAddress"></param>
        /// <param name="organizationSqid"></param>
        /// <param name="email"></param>
        /// <returns></returns>
        Task<bool>InviteUserToOrganizationAsync(string userInformationSqid, string ipAddress, string organizationSqid, string email);
        

        /// <summary>
        /// Redeem an invitation code
        /// </summary>
        /// <param name="userInformationSqid"></param>
        /// <param name="ipAddress"></param>
        /// <param name="invitationCode"></param>
        /// <returns></returns>
        Task<bool> RedeemInvitationAsync(string userInformationSqid, string ipAddress, string invitationCode);

        /// <summary>
        /// Get a list of users that have been invited to join an organization
        /// </summary>
        /// <param name="userInformationSqid"></param>
        /// <param name="ipAddress"></param>
        /// <param name="organizationSqid"></param>
        /// <returns></returns>
        Task<List<OrganizationInvitationCodes>> GetInvitedUserListAsync(string userInformationSqid, string ipAddress, string organizationSqid);

        /// <summary>
        /// Get a list of users in an organization along with their assigned roles
        /// </summary>
        /// <param name="userInformationSqid"></param>
        /// <param name="organizationSqid"></param>
        /// <param name="ipAddress"></param>
        /// <param name="forceCacheRefresh"></param>
        /// <returns></returns>
        Task<List<OrganizationUserRolesDto>> GetOrganizationUserRoles(string userInformationSqid, string organizationSqid, string ipAddress, bool forceCacheRefresh = false);
    }
}
