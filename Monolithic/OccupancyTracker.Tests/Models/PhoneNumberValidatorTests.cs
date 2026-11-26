using FluentAssertions;
using NUnit.Framework;
using OccupancyTracker.Models;

namespace OccupancyTracker.Tests.Models;

public sealed class PhoneNumberValidatorTests
{
    [Test]
    public void Validate_CompletePhoneNumber_ReturnsValid()
    {
        // Arrange
        var phone = new PhoneNumber { CountryCode = "1", Number = "555-0100" };
        var sut = new PhoneNumberValidator();

        // Act
        var result = sut.Validate(phone);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Test]
    public void Validate_MissingPhoneFields_ReturnsInvalid()
    {
        // Arrange
        var phone = new PhoneNumber { CountryCode = "", Number = "" };
        var sut = new PhoneNumberValidator();

        // Act
        var result = sut.Validate(phone);

        // Assert
        result.IsValid.Should().BeFalse();
    }
}
