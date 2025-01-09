using Microsoft.EntityFrameworkCore;
using OccupancyTracker.DTO;
using OccupancyTracker.Models;

namespace OccupancyTracker.IService
{
    public interface IOrganizationUserService
    {
        Task<List<OrganizationUser>> GetUserListForOrganizationAsync(string userInformationSqid, string ipAddress, string organizationSqid, bool forceCacheRefresh = false);

        Task<bool>InviteUserToOrganizationAsync(string userInformationSqid, string ipAddress, string organizationSqid, string email);
        
        Task<bool> RedeemInvitationAsync(string userInformationSqid, string ipAddress, string invitationCode);

        Task<List<OrganizationInvitationCodes>> GetInvitedUserListAsync(string userInformationSqid, string ipAddress, string organizationSqid);

        Task<List<OrganizationUserRolesDto>> GetOrganizationUserRoles(string userInformationSqid, string organizationSqid, string ipAddress, bool forceCacheRefresh = false);
    }
}
