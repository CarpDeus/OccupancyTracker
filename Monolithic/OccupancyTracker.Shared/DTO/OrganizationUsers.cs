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
