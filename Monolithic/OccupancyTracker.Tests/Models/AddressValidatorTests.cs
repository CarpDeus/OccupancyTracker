using FluentAssertions;
using NUnit.Framework;
using OccupancyTracker.Models;

namespace OccupancyTracker.Tests.Models;

public sealed class AddressValidatorTests
{
    [Test]
    public void Validate_CompleteAddress_ReturnsValid()
    {
        // Arrange
        var address = new Address
        {
            AddressLine1 = "123 Main Street",
            AddressLine2 = "Suite 100",
            City = "Seattle",
            State = "Washington",
            PostalCode = "98101",
            Country = "United States"
        };
        var sut = new AddressValidator();

        // Act
        var result = sut.Validate(address);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Test]
    public void Validate_MissingRequiredAddressFields_ReturnsInvalid()
    {
        // Arrange
        var address = new Address
        {
            AddressLine1 = "",
            City = "",
            State = "",
            PostalCode = "",
            Country = ""
        };
        var sut = new AddressValidator();

        // Act
        var result = sut.Validate(address);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Test]
    public void Validate_AddressLineOneOverMaximumLength_ReturnsMaximumLengthError()
    {
        // Arrange
        var address = new Address
        {
            AddressLine1 = new string('a', 1025),
            City = "Seattle",
            State = "Washington",
            PostalCode = "98101",
            Country = "United States"
        };
        var sut = new AddressValidator();

        // Act
        var result = sut.Validate(address);

        // Assert
        result.Errors.Should().Contain(error => error.PropertyName == nameof(Address.AddressLine1));
    }
}
