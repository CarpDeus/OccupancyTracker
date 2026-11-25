namespace OccupancyTracker.DTO;

public sealed class OrganizationUserSummary
{
    public required string UserInformationSqid { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string EmailAddress { get; init; }
    public int UserStatusId { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
}

public sealed class OrganizationInvitationSummary
{
    public required string EmailAddress { get; init; }
    public required string InvitationCode { get; init; }
}

public sealed class InviteOrganizationUser
{
    public required string EmailAddress { get; init; }
}

public sealed class RedeemOrganizationInvitation
{
    public required string InvitationCode { get; init; }
}

public sealed class OrganizationMembership
{
    public required string UserInformationSqid { get; init; }
    public required string Name { get; init; }
    public required string EmailAddress { get; init; }
    public int UserStatusId { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? OrganizationRole { get; init; }
    public required List<MembershipLocationRole> Locations { get; init; }
    public required string Version { get; init; }
}

public sealed class MembershipLocationRole
{
    public required string LocationSqid { get; init; }
    public required string LocationName { get; init; }
    public string? RoleName { get; set; }
}

public sealed class MembershipEdit
{
    public required string Version { get; init; }
    public int UserStatusId { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? OrganizationRole { get; init; }
    public required List<MembershipRoleEdit> LocationRoles { get; init; }
}

public sealed class MembershipRoleEdit
{
    public required string LocationSqid { get; init; }
    public required string RoleName { get; init; }
}
