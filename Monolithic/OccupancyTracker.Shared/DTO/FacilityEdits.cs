using OccupancyTracker.Models;

namespace OccupancyTracker.DTO;

public sealed class LocationEdit
{
    public required string LocationName { get; init; }
    public required string LocationDescription { get; init; }
    public required int MaxOccupancy { get; init; }
    public required int OccupancyThresholdWarning { get; init; }
    public required Address LocationAddress { get; init; }
    public required PhoneNumber PhoneNumber { get; init; }
}

public sealed class EntranceEdit
{
    public required string EntranceName { get; init; }
    public required string EntranceDescription { get; init; }
}
