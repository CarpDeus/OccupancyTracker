using OccupancyTracker.Models;

namespace OccupancyTracker.DTO;

public sealed class OrganizationEdit
{
    public required string OrganizationName { get; init; }
    public required string OrganizationDescription { get; init; }
    public required Address OrganizationAddress { get; init; }
    public required PhoneNumber PhoneNumber { get; init; }
}

public sealed class OrganizationStatusChange
{
    public required int FromStatus { get; init; }
    public required int ToStatus { get; init; }
}
