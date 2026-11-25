using System.Data;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.DTO;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service;

public record FacilityResult<T>(OrganizationAccess Access, T? Value = default);

public sealed class FacilityOperations(
    IDbContextFactory<OccupancyContext> contexts,
    RequestAuthorizationService authorization,
    ISqidsEncoderFactory sqids)
{
    private sealed record Scope(OccupancyContext Db, UserInformation Actor, Organization Org,
        Location? Location, List<CurrentUserRoleInformation> Roles, string OrgRole, string LocationRole);

    private async Task<FacilityResult<T>> ExecuteAsync<T>(string orgSqid, string? locationSqid,
        Func<Scope, Task<FacilityResult<T>>> operation)
    {
        await using var db = await contexts.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await authorization.GetCurrentUserAsync(db);
        if (actor == null) return new(OrganizationAccess.Forbidden);
        var org = await db.Organizations.SingleOrDefaultAsync(x => x.OrganizationSqid == orgSqid);
        if (org == null) return new(OrganizationAccess.NotFound);
        var roles = await AuthorizationGrantStore.GetRolesAsync(db, sqids, actor.UserInformationId);
        var orgRole = ResourceAuthorization.OrganizationRole(roles, orgSqid, actor.IsSuperAdmin);
        if (!CanRead(org.CurrentStatus, orgRole, organization: true))
            return new(OrganizationAccess.Forbidden);
        Location? location = null;
        var locationRole = "";
        if (locationSqid != null)
        {
            location = await db.Locations.SingleOrDefaultAsync(x => x.OrganizationId == org.OrganizationId && x.LocationSqid == locationSqid);
            if (location == null) return new(OrganizationAccess.NotFound);
            locationRole = ResourceAuthorization.LocationRole(roles, orgSqid, locationSqid, actor.IsSuperAdmin);
            if (!CanRead(location.CurrentStatus, locationRole))
                return new(OrganizationAccess.Forbidden);
        }
        var result = await operation(new Scope(db, actor, org, location, roles, orgRole, locationRole));
        if (result.Access == OrganizationAccess.Success) await transaction.CommitAsync();
        return result;
    }

    public Task<FacilityResult<List<Location>>> ListLocationsAsync(string orgSqid) =>
        ExecuteAsync<List<Location>>(orgSqid, null, async scope =>
        {
            var locations = await scope.Db.Locations.Where(x => x.OrganizationId == scope.Org.OrganizationId).ToListAsync();
            return new(OrganizationAccess.Success, locations.Where(x =>
                x.LocationSqid != null && CanRead(x.CurrentStatus,
                    ResourceAuthorization.LocationRole(scope.Roles, orgSqid, x.LocationSqid, scope.Actor.IsSuperAdmin))).ToList());
        });

    public Task<FacilityResult<bool>> CanCreateLocationAsync(string orgSqid) =>
        ExecuteAsync<bool>(orgSqid, null, scope => Task.FromResult(new FacilityResult<bool>(OrganizationAccess.Success,
            scope.Org.CurrentStatus == 0 && (scope.OrgRole == AuthorizationRecords.Roles.OrganizationAdmin.Name ||
                scope.OrgRole == AuthorizationRecords.Roles.SuperAdmin.Name))));

    public Task<FacilityResult<bool>> CanManageLocationAsync(string orgSqid, string locationSqid) =>
        ExecuteAsync<bool>(orgSqid, locationSqid, scope =>
            Task.FromResult(new FacilityResult<bool>(OrganizationAccess.Success, IsAdmin(scope.LocationRole))));

    public Task<FacilityResult<Location>> ReadLocationAsync(string orgSqid, string locationSqid) =>
        ExecuteAsync<Location>(orgSqid, locationSqid, scope => Task.FromResult(new FacilityResult<Location>(OrganizationAccess.Success, scope.Location)));

    public Task<FacilityResult<Location>> CreateLocationAsync(string orgSqid, LocationEdit edit) =>
        ExecuteAsync<Location>(orgSqid, null, async scope =>
        {
            if ((scope.OrgRole != AuthorizationRecords.Roles.OrganizationAdmin.Name &&
                 scope.OrgRole != AuthorizationRecords.Roles.SuperAdmin.Name) || scope.Org.CurrentStatus != 0)
                return new(OrganizationAccess.Forbidden);
            if (!Valid(edit)) return new(OrganizationAccess.Invalid);
            var location = new Location { OrganizationId = scope.Org.OrganizationId, CreatedBy = scope.Actor.UserInformationSqid!, CreatedDate = DateTime.Now };
            Apply(location, edit);
            scope.Db.Locations.Add(location);
            await scope.Db.SaveChangesAsync();
            location.LocationSqid = sqids.EncodeLocationId(location.OrganizationId, location.LocationId);
            await scope.Db.SaveChangesAsync();
            return new(OrganizationAccess.Success, location);
        });

    public Task<FacilityResult<Location>> UpdateLocationAsync(string orgSqid, string locationSqid, LocationEdit edit) =>
        ExecuteAsync<Location>(orgSqid, locationSqid, async scope =>
        {
            if (!IsAdmin(scope.LocationRole)) return new(OrganizationAccess.Forbidden);
            if (!Valid(edit)) return new(OrganizationAccess.Invalid);
            Apply(scope.Location!, edit);
            scope.Location!.ModifiedBy = scope.Actor.UserInformationSqid;
            scope.Location.ModifiedDate = DateTime.Now;
            await scope.Db.SaveChangesAsync();
            return new(OrganizationAccess.Success, scope.Location);
        });

    public Task<FacilityResult<Location>> ChangeLocationStatusAsync(string orgSqid, string locationSqid, OrganizationStatusChange change) =>
        ExecuteAsync<Location>(orgSqid, locationSqid, async scope =>
        {
            if (!IsAdmin(scope.LocationRole)) return new(OrganizationAccess.Forbidden);
            var access = ValidateStatus(scope.Location!.CurrentStatus, change);
            if (access != OrganizationAccess.Success) return new(access);
            scope.Location.CurrentStatus = change.ToStatus;
            scope.Location.ModifiedBy = scope.Actor.UserInformationSqid;
            scope.Location.ModifiedDate = DateTime.Now;
            await scope.Db.SaveChangesAsync();
            return new(OrganizationAccess.Success, scope.Location);
        });

    public Task<FacilityResult<List<Entrance>>> ListEntrancesAsync(string orgSqid, string locationSqid) =>
        ExecuteAsync<List<Entrance>>(orgSqid, locationSqid, async scope =>
        {
            var entrances = await scope.Db.Entrances.Where(x => x.OrganizationId == scope.Org.OrganizationId &&
                x.LocationId == scope.Location!.LocationId).ToListAsync();
            return new(OrganizationAccess.Success, entrances.Where(x => CanRead(x.CurrentStatus, scope.LocationRole)).ToList());
        });

    public Task<FacilityResult<Entrance>> ReadEntranceAsync(string orgSqid, string locationSqid, string entranceSqid) =>
        ExecuteAsync<Entrance>(orgSqid, locationSqid, async scope =>
        {
            var entrance = await FindEntrance(scope, entranceSqid);
            if (entrance == null) return new(OrganizationAccess.NotFound);
            return CanRead(entrance.CurrentStatus, scope.LocationRole)
                ? new(OrganizationAccess.Success, entrance) : new(OrganizationAccess.Forbidden);
        });

    public Task<FacilityResult<Entrance>> CreateEntranceAsync(string orgSqid, string locationSqid, EntranceEdit edit) =>
        ExecuteAsync<Entrance>(orgSqid, locationSqid, async scope =>
        {
            if (!IsAdmin(scope.LocationRole) || scope.Org.CurrentStatus != 0 || scope.Location!.CurrentStatus != 0)
                return new(OrganizationAccess.Forbidden);
            if (!Valid(edit)) return new(OrganizationAccess.Invalid);
            var entrance = new Entrance
            {
                OrganizationId = scope.Org.OrganizationId, LocationId = scope.Location.LocationId,
                EntranceName = edit.EntranceName, EntranceDescription = edit.EntranceDescription,
                CreatedBy = scope.Actor.UserInformationSqid!, CreatedDate = DateTime.Now
            };
            scope.Db.Entrances.Add(entrance);
            await scope.Db.SaveChangesAsync();
            entrance.EntranceSqid = sqids.EncodeEntranceId(entrance.OrganizationId, entrance.LocationId, entrance.EntranceId);
            var counter = new EntranceCounter
            {
                OrganizationId = entrance.OrganizationId, LocationId = entrance.LocationId, EntranceId = entrance.EntranceId,
                CreatedBy = scope.Actor.UserInformationSqid!, CreatedDate = entrance.CreatedDate
            };
            scope.Db.EntranceCounters.Add(counter);
            await scope.Db.SaveChangesAsync();
            counter.EntranceCounterSqid = sqids.EncodeEntranceCounterId(counter.OrganizationId, counter.LocationId, counter.EntranceId, counter.EntranceCounterId);
            entrance.EntranceCounterInstanceSqid = counter.EntranceCounterSqid;
            await scope.Db.SaveChangesAsync();
            return new(OrganizationAccess.Success, entrance);
        });

    public Task<FacilityResult<Entrance>> UpdateEntranceAsync(string orgSqid, string locationSqid, string entranceSqid, EntranceEdit edit) =>
        ExecuteAsync<Entrance>(orgSqid, locationSqid, async scope =>
        {
            if (!IsAdmin(scope.LocationRole)) return new(OrganizationAccess.Forbidden);
            var entrance = await FindEntrance(scope, entranceSqid);
            if (entrance == null) return new(OrganizationAccess.NotFound);
            if (!CanRead(entrance.CurrentStatus, scope.LocationRole)) return new(OrganizationAccess.Forbidden);
            if (!Valid(edit)) return new(OrganizationAccess.Invalid);
            entrance.EntranceName = edit.EntranceName;
            entrance.EntranceDescription = edit.EntranceDescription;
            entrance.ModifiedBy = scope.Actor.UserInformationSqid;
            entrance.ModifiedDate = DateTime.Now;
            await scope.Db.SaveChangesAsync();
            return new(OrganizationAccess.Success, entrance);
        });

    public Task<FacilityResult<Entrance>> ChangeEntranceStatusAsync(string orgSqid, string locationSqid, string entranceSqid, OrganizationStatusChange change) =>
        ExecuteAsync<Entrance>(orgSqid, locationSqid, async scope =>
        {
            if (!IsAdmin(scope.LocationRole)) return new(OrganizationAccess.Forbidden);
            var entrance = await FindEntrance(scope, entranceSqid);
            if (entrance == null) return new(OrganizationAccess.NotFound);
            if (!CanRead(entrance.CurrentStatus, scope.LocationRole)) return new(OrganizationAccess.Forbidden);
            var access = ValidateStatus(entrance.CurrentStatus, change);
            if (access != OrganizationAccess.Success) return new(access);
            entrance.CurrentStatus = change.ToStatus;
            entrance.ModifiedBy = scope.Actor.UserInformationSqid;
            entrance.ModifiedDate = DateTime.Now;
            await scope.Db.SaveChangesAsync();
            return new(OrganizationAccess.Success, entrance);
        });

    private static Task<Entrance?> FindEntrance(Scope scope, string sqid) =>
        scope.Db.Entrances.SingleOrDefaultAsync(x => x.OrganizationId == scope.Org.OrganizationId &&
            x.LocationId == scope.Location!.LocationId && x.EntranceSqid == sqid);

    private static bool IsAdmin(string role) => role == AuthorizationRecords.Roles.SuperAdmin.Name ||
        role == AuthorizationRecords.Roles.OrganizationAdmin.Name || role == AuthorizationRecords.Roles.LocationAdmin.Name;

    private static bool CanRead(int status, string role, bool organization = false) =>
        role == AuthorizationRecords.Roles.SuperAdmin.Name ||
        (status == 0 && !string.IsNullOrEmpty(role)) ||
        (status == 1 && (organization ? role == AuthorizationRecords.Roles.OrganizationAdmin.Name : IsAdmin(role)));

    private static OrganizationAccess ValidateStatus(int current, OrganizationStatusChange change) =>
        change.FromStatus is < 0 or > 2 || !Statuses.DataStatus.ValidChangeStates(change.FromStatus).Contains(change.ToStatus)
            ? OrganizationAccess.Invalid : current != change.FromStatus ? OrganizationAccess.Conflict : OrganizationAccess.Success;

    private static bool Valid(LocationEdit edit)
    {
        if (edit.LocationName == null || edit.LocationDescription == null || edit.LocationAddress == null || edit.PhoneNumber == null)
            return false;
        var location = new Location();
        Apply(location, edit);
        return new LocationValidator().Validate(location).IsValid;
    }

    private static bool Valid(EntranceEdit edit) =>
        edit.EntranceName != null && edit.EntranceDescription != null &&
        new EntranceValidator().Validate(new Entrance { EntranceName = edit.EntranceName, EntranceDescription = edit.EntranceDescription }).IsValid;

    private static void Apply(Location location, LocationEdit edit)
    {
        location.LocationName = edit.LocationName;
        location.LocationDescription = edit.LocationDescription;
        location.LocationAddress = edit.LocationAddress;
        location.PhoneNumber = edit.PhoneNumber;
        location.MaxOccupancy = edit.MaxOccupancy;
        location.OccupancyThresholdWarning = edit.OccupancyThresholdWarning;
    }
}
