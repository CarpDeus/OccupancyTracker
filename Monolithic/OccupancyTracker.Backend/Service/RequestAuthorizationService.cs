using System.Security;
using System.Security.Claims;
using OccupancyTracker.IRepository;
using OccupancyTracker.IService;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service;

/// <summary>
/// Resolves the actor from the authenticated HTTP principal, never from request data.
/// The hosting API must validate the issuer, audience, signature and lifetime before using this service.
/// </summary>
public sealed class RequestAuthorizationService(
    IHttpContextAccessor httpContextAccessor,
    IRepositorySessionFactory contextFactory,
    IOccAuthorizationService authorizationService,
    ISqidsEncoderFactory sqids) : IRequestAuthorizationService
{
    private const string RolesClaim = "https://occupancy-tracker.secure-scalable.solutions/roles";

    public async Task<UserInformation?> GetCurrentUserAsync()
    {
        await using var context = await contextFactory.CreateAsync();
        return await GetCurrentUserAsync(context);
    }

    public async Task<UserInformation?> GetCurrentUserAsync(IRepositorySession context)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        var claims = principal.Identities.Where(identity => identity.IsAuthenticated)
            .SelectMany(identity => identity.Claims).ToList();
        var subjects = claims
            .Where(c => c.Type == ClaimTypes.NameIdentifier || c.Type == "sub")
            .Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();
        if (subjects.Count != 1 || string.IsNullOrWhiteSpace(subjects[0]))
            throw new SecurityException("The authenticated principal must contain exactly one subject.");

        var sso = await context.UserSsoInformation.SingleOrDefaultDetachedAsync(x => x.Auth0Identifier == subjects[0]);
        if (sso?.UserInformationId is not > 0 ||
            !string.Equals(sso.Auth0Identifier, subjects[0], StringComparison.Ordinal))
            throw new SecurityException("The authenticated subject is not linked to an account.");

        var user = await context.UserInformation.SingleOrDefaultDetachedAsync(x => x.UserInformationId == sso.UserInformationId);
        if (user == null || user.CurrentStatusId != Statuses.DataStatus.Active.Id ||
            string.IsNullOrEmpty(user.UserInformationSqid))
            throw new SecurityException("The authenticated account is unavailable.");

        user.IsSuperAdmin = claims.Any(c => c.Type == RolesClaim && c.Value == AuthorizationRecords.Roles.SuperAdmin.Name);
        return user;
    }

    public async Task<string> GetOrganizationRoleAsync(string organizationSqid)
    {
        var actor = await GetCurrentUserAsync();
        if (actor == null || string.IsNullOrEmpty(organizationSqid))
            return string.Empty;

        await using var context = await contextFactory.CreateAsync();
        if (!await context.Organizations.AnyAsync(x => x.OrganizationSqid == organizationSqid))
            return string.Empty;

        var roles = await authorizationService.GetUserRolesFilteredAsync(actor.UserInformationSqid!, organizationSqid);
        return ResourceAuthorization.OrganizationRole(roles, organizationSqid, actor.IsSuperAdmin);
    }

    public async Task<string> GetLocationRoleAsync(string organizationSqid, string locationSqid)
    {
        var actor = await GetCurrentUserAsync();
        if (actor == null || string.IsNullOrEmpty(organizationSqid) || string.IsNullOrEmpty(locationSqid))
            return string.Empty;

        await using var context = await contextFactory.CreateAsync();
        var location = await context.Locations.SingleOrDefaultAsync(x => x.LocationSqid == locationSqid);
        if (location == null || sqids.EncodeOrganizationId(location.OrganizationId) != organizationSqid)
            return string.Empty;

        var roles = await authorizationService.GetUserRolesFilteredAsync(actor.UserInformationSqid!, organizationSqid, locationSqid);
        return ResourceAuthorization.LocationRole(roles, organizationSqid, locationSqid, actor.IsSuperAdmin);
    }

    public async Task<bool> CanUseCounterAsync(string entranceCounterSqid)
    {
        await using var context = await contextFactory.CreateAsync();
        return await CanUseCounterAsync(context, entranceCounterSqid);
    }

    public async Task<bool> CanUseCounterAsync(IRepositorySession context, string entranceCounterSqid)
    {
        if (string.IsNullOrEmpty(entranceCounterSqid))
            return false;

        var data = await LoadCurrentCounterAsync(context, entranceCounterSqid);
        if (data == null)
            return false;
        return await CanUseCounterAsync(context, data);
    }

    internal async Task<bool> CanUseCounterAsync(IRepositorySession context, CurrentCounter data)
    {
        if (!data.Counter.RequiresAuthentication)
            return true;
        var actor = await GetCurrentUserAsync(context);
        if (actor == null) return false;
        var roles = await AuthorizationGrantStore.GetRolesAsync(context, sqids, actor.UserInformationId);
        return !string.IsNullOrEmpty(ResourceAuthorization.LocationRole(
            roles, data.Organization.OrganizationSqid, data.Location.LocationSqid, actor.IsSuperAdmin));
    }

    internal sealed record CurrentCounter(EntranceCounter Counter, Entrance Entrance,
        Location Location, Organization Organization);

    internal static async Task<CurrentCounter?> LoadCurrentCounterAsync(
        IRepositorySession context, string entranceCounterSqid)
    {
        var counters = await context.EntranceCounters.ListAsync(x => x.EntranceCounterSqid == entranceCounterSqid);
        if (counters.Count == 0)
            return null;
        var entranceIds = counters.Select(x => x.EntranceId).Distinct().ToArray();
        var locationIds = counters.Select(x => x.LocationId).Distinct().ToArray();
        var organizationIds = counters.Select(x => x.OrganizationId).Distinct().ToArray();
        var entrances = await context.Entrances.ListAsync(x => entranceIds.Contains(x.EntranceId) &&
            x.EntranceCounterInstanceSqid == entranceCounterSqid);
        var locations = await context.Locations.ListAsync(x => locationIds.Contains(x.LocationId));
        var organizations = await context.Organizations.ListAsync(x => organizationIds.Contains(x.OrganizationId));
        return (from c in counters
            join e in entrances on c.EntranceId equals e.EntranceId
            join l in locations on c.LocationId equals l.LocationId
            join o in organizations on c.OrganizationId equals o.OrganizationId
            where e.LocationId == l.LocationId && e.OrganizationId == o.OrganizationId &&
                l.OrganizationId == o.OrganizationId &&
                c.CurrentStatus == 0 && e.CurrentStatus == 0 && l.CurrentStatus == 0 && o.CurrentStatus == 0
            select new CurrentCounter(c, e, l, o)).SingleOrDefault();
    }
}
