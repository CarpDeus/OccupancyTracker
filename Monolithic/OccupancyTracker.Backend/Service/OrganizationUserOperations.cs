using System.Data;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
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

    private sealed record MembershipData(OrganizationUser Membership, List<OrganizationUserRole> Roles, List<Location> Locations);

    private static bool ValidOrganizationRole(string? role) => role == null ||
        role == AuthorizationRecords.Roles.User.Name ||
        role == AuthorizationRecords.Roles.LocationAdmin.Name ||
        role == AuthorizationRecords.Roles.OrganizationAdmin.Name;

    private static bool ValidLocationRole(string? role) =>
        role == AuthorizationRecords.Roles.User.Name || role == AuthorizationRecords.Roles.LocationAdmin.Name;

    private static string MembershipVersion(MembershipData data)
    {
        var state = new
        {
            data.Membership.OrganizationUsersId,
            data.Membership.UserStatusId,
            data.Membership.StartDate,
            data.Membership.EndDate,
            Roles = data.Roles.OrderBy(x => x.OrganizationUserRoleId)
                .Select(x => new { x.OrganizationUserRoleId, x.OrganizationWide, x.LocationId, x.RoleName })
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state))));
    }

    private static async Task<FacilityResult<MembershipData>> LoadMembershipAsync(Scope scope, string userSqid)
    {
        var matches = await scope.Db.OrganizationUsers.Include(x => x.UserInformation)
            .Where(x => x.OrganizationId == scope.Organization.OrganizationId &&
                x.UserInformation.UserInformationSqid == userSqid).ToListAsync();
        if (matches.Count == 0 || !string.Equals(matches[0].UserInformation.UserInformationSqid, userSqid, StringComparison.Ordinal))
            return new(OrganizationAccess.NotFound);
        if (matches.Count != 1) return new(OrganizationAccess.Conflict);
        var membership = matches[0];
        var roles = await scope.Db.OrganizationUserRoles
            .Where(x => x.OrganizationUserId == membership.OrganizationUsersId).ToListAsync();
        var locations = await scope.Db.Locations.AsNoTracking()
            .Where(x => x.OrganizationId == scope.Organization.OrganizationId).ToListAsync();
        var locationIds = locations.Select(x => x.LocationId).ToHashSet();
        if (locations.Any(x => string.IsNullOrEmpty(x.LocationSqid)) ||
            locations.Select(x => x.LocationSqid).Distinct(StringComparer.Ordinal).Count() != locations.Count ||
            roles.Any(x => x.OrganizationWide
                ? x.LocationId != null || !ValidOrganizationRole(x.RoleName) || x.RoleName == null
                : !x.LocationId.HasValue || !locationIds.Contains(x.LocationId.Value) || !ValidLocationRole(x.RoleName)) ||
            roles.Count(x => x.OrganizationWide) > 1 ||
            roles.Where(x => !x.OrganizationWide).GroupBy(x => x.LocationId).Any(g => g.Count() > 1))
            return new(OrganizationAccess.Conflict);
        return new(OrganizationAccess.Success, new MembershipData(membership, roles, locations));
    }

    private static OrganizationMembership DescribeMembership(MembershipData data) => new()
    {
        UserInformationSqid = data.Membership.UserInformation.UserInformationSqid!,
        Name = $"{data.Membership.UserInformation.FirstName} {data.Membership.UserInformation.LastName}".Trim(),
        EmailAddress = data.Membership.UserInformation.EmailAddress,
        UserStatusId = data.Membership.UserStatusId,
        StartDate = data.Membership.StartDate,
        EndDate = data.Membership.EndDate,
        OrganizationRole = data.Roles.SingleOrDefault(x => x.OrganizationWide)?.RoleName,
        Locations = data.Locations.OrderBy(x => x.LocationName).Select(location => new MembershipLocationRole
        {
            LocationSqid = location.LocationSqid!,
            LocationName = location.LocationName,
            RoleName = data.Roles.SingleOrDefault(x => !x.OrganizationWide && x.LocationId == location.LocationId)?.RoleName
        }).ToList(),
        Version = MembershipVersion(data)
    };

    public Task<FacilityResult<OrganizationMembership>> ReadMembershipAsync(string orgSqid, string userSqid) =>
        ExecuteAdminAsync<OrganizationMembership>(orgSqid, async scope =>
        {
            var loaded = await LoadMembershipAsync(scope, userSqid);
            return loaded.Access == OrganizationAccess.Success
                ? new(OrganizationAccess.Success, DescribeMembership(loaded.Value!))
                : new(loaded.Access);
        });

    public Task<FacilityResult<OrganizationMembership>> UpdateMembershipAsync(string orgSqid, string userSqid, MembershipEdit edit) =>
        ExecuteAdminAsync<OrganizationMembership>(orgSqid, async scope =>
        {
            var loaded = await LoadMembershipAsync(scope, userSqid);
            if (loaded.Access != OrganizationAccess.Success) return new(loaded.Access);
            var data = loaded.Value!;
            if (data.Membership.UserStatusId is not (0 or 1)) return new(OrganizationAccess.Conflict);
            if (string.IsNullOrEmpty(edit.Version) || edit.LocationRoles == null ||
                edit.UserStatusId is not (0 or 1) || edit.StartDate > edit.EndDate ||
                !ValidOrganizationRole(edit.OrganizationRole) ||
                edit.LocationRoles.Any(x => x == null || string.IsNullOrEmpty(x.LocationSqid) || !ValidLocationRole(x.RoleName)) ||
                edit.LocationRoles.Select(x => x.LocationSqid).Distinct(StringComparer.Ordinal).Count() != edit.LocationRoles.Count)
                return new(OrganizationAccess.Invalid);
            if (!string.Equals(edit.Version, MembershipVersion(data), StringComparison.Ordinal))
                return new(OrganizationAccess.Conflict);
            var locations = data.Locations.ToDictionary(x => x.LocationSqid!, StringComparer.Ordinal);
            if (edit.LocationRoles.Any(x => !locations.ContainsKey(x.LocationSqid)))
                return new(OrganizationAccess.Invalid);
            if (!await PreservesAdminCoverageAsync(scope, data, edit))
                return new(OrganizationAccess.Conflict);

            data.Membership.UserStatusId = edit.UserStatusId;
            data.Membership.StartDate = edit.StartDate;
            data.Membership.EndDate = edit.EndDate;
            data.Membership.ModifiedBy = scope.Actor.UserInformationSqid;
            data.Membership.ModifiedDate = DateTime.UtcNow;
            scope.Db.OrganizationUserRoles.RemoveRange(data.Roles);
            var newRoles = new List<OrganizationUserRole>();
            if (edit.OrganizationRole != null)
                newRoles.Add(new OrganizationUserRole { OrganizationUserId = data.Membership.OrganizationUsersId,
                    OrganizationWide = true, RoleName = edit.OrganizationRole });
            newRoles.AddRange(edit.LocationRoles.Select(x => new OrganizationUserRole
            {
                OrganizationUserId = data.Membership.OrganizationUsersId, LocationId = locations[x.LocationSqid].LocationId,
                OrganizationWide = false, RoleName = x.RoleName
            }));
            scope.Db.OrganizationUserRoles.AddRange(newRoles);
            await scope.Db.SaveChangesAsync();
            return new(OrganizationAccess.Success, DescribeMembership(new MembershipData(data.Membership, newRoles, data.Locations)));
        });

    private static async Task<bool> PreservesAdminCoverageAsync(Scope scope, MembershipData data, MembershipEdit edit)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;
        var old = data.Membership;
        if (old.UserStatusId != AuthorizationRecords.UserStatus.Approved.Id ||
            old.UserInformation.CurrentStatusId != Statuses.DataStatus.Active.Id ||
            !data.Roles.Any(x => x.OrganizationWide && x.RoleName == AuthorizationRecords.Roles.OrganizationAdmin.Name))
            return true;
        var start = Math.Max(today, old.StartDate?.DayNumber ?? today);
        var end = old.EndDate?.DayNumber ?? DateOnly.MaxValue.DayNumber;
        if (start > end) return true;

        var others = await (from membership in scope.Db.OrganizationUsers
            join role in scope.Db.OrganizationUserRoles on membership.OrganizationUsersId equals role.OrganizationUserId
            where membership.OrganizationId == scope.Organization.OrganizationId &&
                membership.OrganizationUsersId != old.OrganizationUsersId &&
                membership.UserStatusId == AuthorizationRecords.UserStatus.Approved.Id &&
                membership.UserInformation.CurrentStatusId == Statuses.DataStatus.Active.Id &&
                role.OrganizationWide && role.RoleName == AuthorizationRecords.Roles.OrganizationAdmin.Name
            select new { membership.StartDate, membership.EndDate, role.RoleName }).ToListAsync();
        var intervals = others.Where(x => x.RoleName == AuthorizationRecords.Roles.OrganizationAdmin.Name)
            .Select(x => (Start: x.StartDate?.DayNumber ?? 0,
            End: x.EndDate?.DayNumber ?? DateOnly.MaxValue.DayNumber)).ToList();
        if (edit.UserStatusId == AuthorizationRecords.UserStatus.Approved.Id &&
            edit.OrganizationRole == AuthorizationRecords.Roles.OrganizationAdmin.Name)
            intervals.Add((edit.StartDate?.DayNumber ?? 0, edit.EndDate?.DayNumber ?? DateOnly.MaxValue.DayNumber));

        var next = start;
        foreach (var interval in intervals.OrderBy(x => x.Start))
        {
            if (interval.End < next) continue;
            if (interval.Start > next) return false;
            if (interval.End >= end) return true;
            next = interval.End + 1;
        }
        return false;
    }

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
