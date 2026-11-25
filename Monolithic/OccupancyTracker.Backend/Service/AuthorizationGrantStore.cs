using Microsoft.EntityFrameworkCore;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service;

internal static class AuthorizationGrantStore
{
    public static async Task<List<CurrentUserRoleInformation>> GetRolesAsync(
        OccupancyContext context, ISqidsEncoderFactory sqids, long userId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await (from role in context.OrganizationUserRoles
            join membership in context.OrganizationUsers on role.OrganizationUserId equals membership.OrganizationUsersId
            where membership.UserInformationId == userId &&
                membership.UserStatusId == AuthorizationRecords.UserStatus.Approved.Id &&
                (!membership.StartDate.HasValue || membership.StartDate <= today) &&
                (!membership.EndDate.HasValue || membership.EndDate >= today)
            select new CurrentUserRoleInformation
            {
                OrganizationSqid = sqids.EncodeOrganizationId(membership.OrganizationId),
                LocationSqid = role.Location != null ? role.Location.LocationSqid : null,
                OrganizationWide = role.OrganizationWide,
                RoleName = role.RoleName
            }).ToListAsync();
    }
}
