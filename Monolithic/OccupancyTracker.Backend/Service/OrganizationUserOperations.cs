using System.Data;
using System.Net.Mail;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.DTO;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service;

public sealed class OrganizationUserOperations(
    IDbContextFactory<OccupancyContext> contexts,
    RequestAuthorizationService authorization,
    ISqidsEncoderFactory sqids,
    IConfiguration configuration)
{
    private sealed record Scope(OccupancyContext Db, UserInformation Actor, Organization Organization);

    private async Task<FacilityResult<T>> ExecuteAdminAsync<T>(string orgSqid, Func<Scope, Task<FacilityResult<T>>> operation)
    {
        await using var db = await contexts.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await authorization.GetCurrentUserAsync(db);
        if (actor == null) return new(OrganizationAccess.Forbidden);
        var org = await db.Organizations.SingleOrDefaultAsync(x => x.OrganizationSqid == orgSqid);
        if (org == null) return new(OrganizationAccess.NotFound);
        var roles = await AuthorizationGrantStore.GetRolesAsync(db, sqids, actor.UserInformationId);
        var role = ResourceAuthorization.OrganizationRole(roles, orgSqid, actor.IsSuperAdmin);
        if (role != AuthorizationRecords.Roles.SuperAdmin.Name &&
            (role != AuthorizationRecords.Roles.OrganizationAdmin.Name || org.CurrentStatus is not (0 or 1)))
            return new(OrganizationAccess.Forbidden);
        var result = await operation(new Scope(db, actor, org));
        if (result.Access == OrganizationAccess.Success) await transaction.CommitAsync();
        return result;
    }

    public async Task<FacilityResult<bool>> CanManageAsync(string orgSqid)
    {
        await using var db = await contexts.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await authorization.GetCurrentUserAsync(db);
        if (actor == null) return new(OrganizationAccess.Forbidden);
        var org = await db.Organizations.SingleOrDefaultAsync(x => x.OrganizationSqid == orgSqid);
        if (org == null) return new(OrganizationAccess.NotFound);
        var roles = await AuthorizationGrantStore.GetRolesAsync(db, sqids, actor.UserInformationId);
        var role = ResourceAuthorization.OrganizationRole(roles, orgSqid, actor.IsSuperAdmin);
        var admin = role == AuthorizationRecords.Roles.SuperAdmin.Name ||
            (role == AuthorizationRecords.Roles.OrganizationAdmin.Name && org.CurrentStatus is 0 or 1);
        if (!admin && (string.IsNullOrEmpty(role) || org.CurrentStatus != 0))
            return new(OrganizationAccess.Forbidden);
        await transaction.CommitAsync();
        return new(OrganizationAccess.Success, admin);
    }

    public Task<FacilityResult<List<OrganizationUserSummary>>> ListUsersAsync(string orgSqid) =>
        ExecuteAdminAsync<List<OrganizationUserSummary>>(orgSqid, async scope =>
        {
            var users = await scope.Db.OrganizationUsers.AsNoTracking()
                .Where(x => x.OrganizationId == scope.Organization.OrganizationId)
                .Select(x => new OrganizationUserSummary
                {
                    UserInformationSqid = x.UserInformation.UserInformationSqid!,
                    FirstName = x.UserInformation.FirstName, LastName = x.UserInformation.LastName,
                    EmailAddress = x.UserInformation.EmailAddress, UserStatusId = x.UserStatusId,
                    StartDate = x.StartDate, EndDate = x.EndDate
                }).ToListAsync();
            return new(OrganizationAccess.Success, users);
        });

    public Task<FacilityResult<List<OrganizationInvitationSummary>>> ListInvitationsAsync(string orgSqid) =>
        ExecuteAdminAsync<List<OrganizationInvitationSummary>>(orgSqid, async scope =>
        {
            var invitations = await scope.Db.OrganizationInvitationCodes.AsNoTracking()
                .Where(x => x.OrganizationId == scope.Organization.OrganizationId &&
                    x.CurrentStatus == Statuses.DataStatus.Active.Id && !x.InvitationRedeemed.HasValue)
                .Select(x => new OrganizationInvitationSummary { EmailAddress = x.EmailAddress, InvitationCode = x.InvitationCode! })
                .ToListAsync();
            return new(OrganizationAccess.Success, invitations);
        });

    public Task<FacilityResult<bool>> InviteAsync(string orgSqid, InviteOrganizationUser input) =>
        ExecuteAdminAsync<bool>(orgSqid, async scope =>
        {
            if (scope.Organization.CurrentStatus != Statuses.DataStatus.Active.Id) return new(OrganizationAccess.Forbidden);
            var email = input.EmailAddress?.Trim();
            if (string.IsNullOrEmpty(email) || email.Length > 256 ||
                !MailAddress.TryCreate(email, out var address) || address.Address != email || !string.IsNullOrEmpty(address.DisplayName))
                return new(OrganizationAccess.Invalid);
            var domain = configuration["OccupancyDomain"];
            if (!Uri.TryCreate($"https://{domain}/", UriKind.Absolute, out var authority) ||
                authority.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(authority.UserInfo) ||
                authority.AbsolutePath != "/" || !string.IsNullOrEmpty(authority.Query) || !string.IsNullOrEmpty(authority.Fragment))
                throw new InvalidOperationException("OccupancyDomain must be a hostname.");
            var invitation = new OrganizationInvitationCodes
            {
                OrganizationId = scope.Organization.OrganizationId, EmailAddress = email,
                CreatedBy = scope.Actor.UserInformationSqid!, CreatedDate = DateTime.UtcNow
            };
            scope.Db.OrganizationInvitationCodes.Add(invitation);
            await scope.Db.SaveChangesAsync();
            invitation.InvitationCode = sqids.EncodeInvitationId(invitation.OrganizationId, invitation.OrganizationInvitationCodeId);
            var link = new Uri(authority, $"profile/edit?invitationCode={Uri.EscapeDataString(invitation.InvitationCode)}").AbsoluteUri;
            var name = HtmlEncoder.Default.Encode(scope.Organization.OrganizationName);
            var safeLink = HtmlEncoder.Default.Encode(link);
            var mail = new SendGridData
            {
                FromEmailAddress = "websupport@secure-scalable.solutions", FromName = "WebSupport",
                Subject = $"Invitation to join Occupancy Tracker for {scope.Organization.OrganizationName}",
                HtmlContent = $"<p>You have been invited to join Occupancy Tracker for {name}. <a href=\"{safeLink}\">Accept the invitation</a>.</p>",
                PlainTextContent = $"You have been invited to join Occupancy Tracker for {scope.Organization.OrganizationName}. Open {link} or enter invitation code {invitation.InvitationCode} on your profile.",
                ToEmailAddress = email
            };
            scope.Db.EmailProcessorQueue.Add(new EmailProcessorQueue
            {
                EmailProcessorData = JsonSerializer.Serialize(mail), CreatedBy = scope.Actor.UserInformationId,
                CreatedDate = DateTime.UtcNow, OrganizationId = scope.Organization.OrganizationId
            });
            await scope.Db.SaveChangesAsync();
            return new(OrganizationAccess.Success, true);
        });

    public async Task<FacilityResult<bool>> RedeemAsync(RedeemOrganizationInvitation input)
    {
        if (string.IsNullOrWhiteSpace(input.InvitationCode)) return new(OrganizationAccess.Invalid);
        await using var db = await contexts.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await authorization.GetCurrentUserAsync(db);
        if (actor == null || !actor.HasCompletedRegistration) return new(OrganizationAccess.Forbidden);
        var invitation = await db.OrganizationInvitationCodes.SingleOrDefaultAsync(x => x.InvitationCode == input.InvitationCode);
        if (invitation == null || !string.Equals(invitation.InvitationCode, input.InvitationCode, StringComparison.Ordinal))
            return new(OrganizationAccess.NotFound);
        if (!string.Equals(invitation.EmailAddress, actor.EmailAddress, StringComparison.OrdinalIgnoreCase))
            return new(OrganizationAccess.Forbidden);
        var organization = await db.Organizations.SingleOrDefaultAsync(x => x.OrganizationId == invitation.OrganizationId);
        if (organization == null) return new(OrganizationAccess.NotFound);
        if (invitation.CurrentStatus != Statuses.DataStatus.Active.Id || invitation.InvitationRedeemed.HasValue ||
            organization.CurrentStatus != Statuses.DataStatus.Active.Id)
            return new(OrganizationAccess.Conflict);
        var memberships = await db.OrganizationUsers.Where(x => x.OrganizationId == organization.OrganizationId &&
            x.UserInformationId == actor.UserInformationId).ToListAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (memberships.Any(x => x.UserStatusId != AuthorizationRecords.UserStatus.Approved.Id ||
            (x.StartDate.HasValue && x.StartDate > today) || (x.EndDate.HasValue && x.EndDate < today)))
            return new(OrganizationAccess.Forbidden);
        if (memberships.Count == 0)
        {
            var membership = new OrganizationUser
            {
                OrganizationId = organization.OrganizationId, UserInformationId = actor.UserInformationId,
                CreatedBy = actor.UserInformationSqid!, CreatedDate = DateTime.UtcNow
            };
            db.OrganizationUsers.Add(membership);
            await db.SaveChangesAsync();
            db.OrganizationUserRoles.Add(new OrganizationUserRole
            {
                OrganizationUserId = membership.OrganizationUsersId,
                RoleName = AuthorizationRecords.Roles.User.Name, OrganizationWide = true
            });
        }
        var storedActor = await db.UserInformation.SingleAsync(x => x.UserInformationId == actor.UserInformationId);
        storedActor.BelongsToOrganization = true;
        invitation.InvitationRedeemed = today;
        invitation.ModifiedBy = actor.UserInformationSqid;
        invitation.ModifiedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(OrganizationAccess.Success, true);
    }
}
