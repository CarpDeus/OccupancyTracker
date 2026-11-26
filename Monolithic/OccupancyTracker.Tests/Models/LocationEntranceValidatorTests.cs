using FluentAssertions;
using NUnit.Framework;
using OccupancyTracker.Models;

namespace OccupancyTracker.Tests.Models;

public sealed class LocationEntranceValidatorTests
{
    [Test]
    public void LocationValidator_MaxOccupancyBelowOne_ReturnsInvalid()
    {
        // Arrange
        var location = new Location
        {
            LocationName = "Lobby",
            MaxOccupancy = 0,
            OccupancyThresholdWarning = 0,
            LocationAddress = new Address
            {
                AddressLine1 = "123 Main Street",
                City = "Seattle",
                State = "Washington",
                PostalCode = "98101",
                Country = "United States"
            },
            PhoneNumber = new PhoneNumber { CountryCode = "1", Number = "555-0100" }
        };
        var sut = new LocationValidator();

        // Act
        var result = sut.Validate(location);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Test]
    public void EntranceValidator_NameOverMaximumLength_ReturnsInvalid()
    {
        // Arrange
        var entrance = new Entrance
        {
            EntranceName = new string('x', 257),
            EntranceDescription = "Main entrance"
        };
        var sut = new EntranceValidator();

        // Act
        var result = sut.Validate(entrance);

        // Assert
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(Entrance.EntranceName));
    }
}
