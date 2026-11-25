using System.Data;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.DTO;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service;

public enum OrganizationAccess { Success, NotFound, Forbidden, Invalid, Conflict }
public record OrganizationResult(OrganizationAccess Access, Organization? Organization = null);

public sealed class OrganizationOperations(
    IDbContextFactory<OccupancyContext> contexts,
    RequestAuthorizationService authorization,
    ISqidsEncoderFactory sqids)
{
    public async Task<OrganizationResult> ReadAsync(string sqid)
    {
        await using var context = await contexts.CreateDbContextAsync();
        var actor = await authorization.GetCurrentUserAsync(context);
        if (actor == null) return new(OrganizationAccess.Forbidden);
        var organization = await context.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationSqid == sqid);
        if (organization == null) return new(OrganizationAccess.NotFound);
        var role = await GetRoleAsync(context, actor, sqid);
        return CanRead(organization, role) ? new(OrganizationAccess.Success, organization) : new(OrganizationAccess.Forbidden);
    }

    public async Task<OrganizationResult> CreateAsync(OrganizationEdit input)
    {
        if (!IsValid(input)) return new(OrganizationAccess.Invalid);
        await using var context = await contexts.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await authorization.GetCurrentUserAsync(context);
        if (actor == null || !actor.HasCompletedRegistration ||
            (!actor.IsSuperAdmin && (actor.BelongsToOrganization ||
                await context.OrganizationUsers.AnyAsync(x => x.UserInformationId == actor.UserInformationId))))
            return new(OrganizationAccess.Forbidden);

        var organization = new Organization { CreatedBy = actor.UserInformationSqid!, CreatedDate = DateTime.Now };
        ApplyEdit(organization, input);
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        organization.OrganizationSqid = sqids.EncodeOrganizationId(organization.OrganizationId);
        var membership = new OrganizationUser
        {
            OrganizationId = organization.OrganizationId, UserInformationId = actor.UserInformationId,
            CreatedBy = actor.UserInformationSqid!, CreatedDate = DateTime.UtcNow
        };
        context.OrganizationUsers.Add(membership);
        await context.SaveChangesAsync();
        context.OrganizationUserRoles.Add(new OrganizationUserRole
        {
            OrganizationUserId = membership.OrganizationUsersId,
            RoleName = AuthorizationRecords.Roles.OrganizationAdmin.Name,
            OrganizationWide = true
        });
        var storedActor = await context.UserInformation.SingleAsync(x => x.UserInformationId == actor.UserInformationId);
        storedActor.BelongsToOrganization = true;
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(OrganizationAccess.Success, organization);
    }

    public async Task<OrganizationResult> UpdateAsync(string sqid, OrganizationEdit input)
    {
        if (!IsValid(input)) return new(OrganizationAccess.Invalid);
        await using var context = await contexts.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await authorization.GetCurrentUserAsync(context);
        if (actor == null) return new(OrganizationAccess.Forbidden);
        var organization = await context.Organizations.SingleOrDefaultAsync(x => x.OrganizationSqid == sqid);
        if (organization == null) return new(OrganizationAccess.NotFound);
        var role = await GetRoleAsync(context, actor, sqid);
        if (!CanAdminister(organization, role)) return new(OrganizationAccess.Forbidden);
        ApplyEdit(organization, input);
        organization.ModifiedBy = actor.UserInformationSqid;
        organization.ModifiedDate = DateTime.Now;
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(OrganizationAccess.Success, organization);
    }

    public async Task<OrganizationResult> ChangeStatusAsync(string sqid, OrganizationStatusChange change)
    {
        if (change.FromStatus is < 0 or > 2 || !Statuses.DataStatus.ValidChangeStates(change.FromStatus).Contains(change.ToStatus))
            return new(OrganizationAccess.Invalid);
        await using var context = await contexts.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await authorization.GetCurrentUserAsync(context);
        if (actor == null) return new(OrganizationAccess.Forbidden);
        var organization = await context.Organizations.SingleOrDefaultAsync(x => x.OrganizationSqid == sqid);
        if (organization == null) return new(OrganizationAccess.NotFound);
        var role = await GetRoleAsync(context, actor, sqid);
        if (!CanAdminister(organization, role)) return new(OrganizationAccess.Forbidden);
        if (organization.CurrentStatus != change.FromStatus) return new(OrganizationAccess.Conflict);
        organization.CurrentStatus = change.ToStatus;
        organization.ModifiedBy = actor.UserInformationSqid;
        organization.ModifiedDate = DateTime.Now;
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(OrganizationAccess.Success, organization);
    }

    private async Task<string> GetRoleAsync(OccupancyContext context, UserInformation actor, string sqid)
    {
        var roles = await AuthorizationGrantStore.GetRolesAsync(context, sqids, actor.UserInformationId);
        return ResourceAuthorization.OrganizationRole(roles, sqid, actor.IsSuperAdmin);
    }

    private static bool CanRead(Organization organization, string role) =>
        role == AuthorizationRecords.Roles.SuperAdmin.Name ||
        (role == AuthorizationRecords.Roles.OrganizationAdmin.Name && organization.CurrentStatus == 1) ||
        (!string.IsNullOrEmpty(role) && organization.CurrentStatus == 0);

    private static bool CanAdminister(Organization organization, string role) =>
        role == AuthorizationRecords.Roles.SuperAdmin.Name ||
        (role == AuthorizationRecords.Roles.OrganizationAdmin.Name &&
            (organization.CurrentStatus == 0 || organization.CurrentStatus == 1));

    private static bool IsValid(OrganizationEdit input)
    {
        if (input.OrganizationName == null || input.OrganizationDescription == null ||
            input.OrganizationAddress == null || input.PhoneNumber == null) return false;
        var organization = new Organization();
        ApplyEdit(organization, input);
        return new OrganizationValidator().Validate(organization).IsValid;
    }

    private static void ApplyEdit(Organization organization, OrganizationEdit input)
    {
        organization.OrganizationName = input.OrganizationName;
        organization.OrganizationDescription = input.OrganizationDescription;
        organization.OrganizationAddress = input.OrganizationAddress;
        organization.PhoneNumber = input.PhoneNumber;
    }
}
