using OccupancyTracker.IRepository;
using OccupancyTracker.IService;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service;

public sealed class OrganizationListingService(IRepositorySessionFactory repositories,
    RequestAuthorizationService requestAuthorization, IOccAuthorizationService authorization)
{
    public async Task<List<Organization>?> ListAsync()
    {
        var actor = await requestAuthorization.GetCurrentUserAsync();
        if (actor == null) return null;
        await using var session = await repositories.CreateAsync();
        if (actor.IsSuperAdmin) return await session.Organizations.ListAsync();
        var roles = await authorization.GetUserRolesFilteredAsync(actor.UserInformationSqid!);
        var visible = roles.Select(x => x.OrganizationSqid).Distinct()
            .Where(id => !string.IsNullOrEmpty(ResourceAuthorization.OrganizationRole(roles, id, false))).ToList();
        var administrators = visible.Where(id => ResourceAuthorization.OrganizationRole(roles, id, false) ==
            AuthorizationRecords.Roles.OrganizationAdmin.Name).ToList();
        return await session.Organizations.ListAsync(x => x.OrganizationSqid != null &&
            ((x.CurrentStatus == 0 && visible.Contains(x.OrganizationSqid)) ||
             (x.CurrentStatus == 1 && administrators.Contains(x.OrganizationSqid))));
    }
}
