using System.Security;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.IService;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service;

/// <summary>
/// Resolves the actor from the authenticated HTTP principal, never from request data.
/// The hosting API must validate the issuer, audience, signature and lifetime before using this service.
/// </summary>
public sealed class RequestAuthorizationService(
    IHttpContextAccessor httpContextAccessor,
    IDbContextFactory<OccupancyContext> contextFactory,
    IOccAuthorizationService authorizationService,
    ISqidsEncoderFactory sqids) : IRequestAuthorizationService
{
    private const string RolesClaim = "https://occupancy-tracker.secure-scalable.solutions/roles";

    public async Task<UserInformation?> GetCurrentUserAsync()
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

        await using var context = await contextFactory.CreateDbContextAsync();
        var sso = await context.UserSsoInformation.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Auth0Identifier == subjects[0]);
        if (sso?.UserInformationId is not > 0 ||
            !string.Equals(sso.Auth0Identifier, subjects[0], StringComparison.Ordinal))
            throw new SecurityException("The authenticated subject is not linked to an account.");

        var user = await context.UserInformation.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserInformationId == sso.UserInformationId);
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

        await using var context = await contextFactory.CreateDbContextAsync();
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

        await using var context = await contextFactory.CreateDbContextAsync();
        var location = await context.Locations.AsNoTracking()
            .Where(x => x.LocationSqid == locationSqid)
            .Select(x => new { x.OrganizationId }).SingleOrDefaultAsync();
        if (location == null || sqids.EncodeOrganizationId(location.OrganizationId) != organizationSqid)
            return string.Empty;

        var roles = await authorizationService.GetUserRolesFilteredAsync(actor.UserInformationSqid!, organizationSqid, locationSqid);
        return ResourceAuthorization.LocationRole(roles, organizationSqid, locationSqid, actor.IsSuperAdmin);
    }

    public async Task<bool> CanUseCounterAsync(string entranceCounterSqid)
    {
        if (string.IsNullOrEmpty(entranceCounterSqid))
            return false;

        await using var context = await contextFactory.CreateDbContextAsync();
        var counter = await (from c in context.EntranceCounters.AsNoTracking()
            join e in context.Entrances.AsNoTracking() on c.EntranceId equals e.EntranceId
            join l in context.Locations.AsNoTracking() on c.LocationId equals l.LocationId
            join o in context.Organizations.AsNoTracking() on c.OrganizationId equals o.OrganizationId
            where c.EntranceCounterSqid == entranceCounterSqid &&
                e.EntranceCounterInstanceSqid == entranceCounterSqid &&
                e.LocationId == l.LocationId && e.OrganizationId == o.OrganizationId &&
                l.OrganizationId == o.OrganizationId &&
                c.CurrentStatus == 0 && e.CurrentStatus == 0 && l.CurrentStatus == 0 && o.CurrentStatus == 0
            select new { c.RequiresAuthentication, o.OrganizationSqid, l.LocationSqid })
            .SingleOrDefaultAsync();

        if (counter == null)
            return false;
        if (!counter.RequiresAuthentication)
            return true;
        return !string.IsNullOrEmpty(await GetLocationRoleAsync(counter.OrganizationSqid, counter.LocationSqid));
    }
}
