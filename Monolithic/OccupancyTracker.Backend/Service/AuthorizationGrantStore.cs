using OccupancyTracker.IRepository;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service;

internal static class AuthorizationGrantStore
{
    public static async Task<List<CurrentUserRoleInformation>> GetRolesAsync(
        IRepositorySession context, ISqidsEncoderFactory sqids, long userId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var memberships = await context.OrganizationUsers.ListAsync(x => x.UserInformationId == userId);
        var membershipIds = memberships.Select(x => x.OrganizationUsersId).ToArray();
        if (membershipIds.Length == 0)
            return [];
        var roles = await context.OrganizationUserRoles.ListAsync(
            x => membershipIds.Contains(x.OrganizationUserId), default, x => x.Location);
        var activeMemberships = memberships.Where(membership =>
            membership.UserStatusId == AuthorizationRecords.UserStatus.Approved.Id &&
            (!membership.StartDate.HasValue || membership.StartDate <= today) &&
            (!membership.EndDate.HasValue || membership.EndDate >= today))
            .ToDictionary(x => x.OrganizationUsersId);
        return roles.Where(role => activeMemberships.ContainsKey(role.OrganizationUserId))
            .Select(role => new CurrentUserRoleInformation
            {
                OrganizationSqid = sqids.EncodeOrganizationId(activeMemberships[role.OrganizationUserId].OrganizationId),
                LocationSqid = role.Location != null ? role.Location.LocationSqid : null,
                OrganizationWide = role.OrganizationWide,
                RoleName = role.RoleName
            }).ToList();
    }
}
