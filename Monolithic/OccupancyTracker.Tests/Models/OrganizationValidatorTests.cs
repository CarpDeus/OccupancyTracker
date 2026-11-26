using FluentAssertions;
using NUnit.Framework;
using OccupancyTracker.Models;

namespace OccupancyTracker.Tests.Models;

public sealed class OrganizationValidatorTests
{
    [Test]
    public void Validate_CompleteOrganization_ReturnsValid()
    {
        // Arrange
        var organization = new Organization
        {
            OrganizationName = "Example Organization",
            OrganizationDescription = "Example",
            OrganizationAddress = new Address
            {
                AddressLine1 = "123 Main Street",
                City = "Seattle",
                State = "Washington",
                PostalCode = "98101",
                Country = "United States"
            },
            PhoneNumber = new PhoneNumber { CountryCode = "1", Number = "555-0100" }
        };
        var sut = new OrganizationValidator();

        // Act
        var result = sut.Validate(organization);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Test]
    public void Validate_MissingOrganizationName_ReturnsRequiredError()
    {
        // Arrange
        var organization = new Organization
        {
            OrganizationName = "",
            OrganizationAddress = new Address
            {
                AddressLine1 = "123 Main Street",
                City = "Seattle",
                State = "Washington",
                PostalCode = "98101",
                Country = "United States"
            },
            PhoneNumber = new PhoneNumber { CountryCode = "1", Number = "555-0100" }
        };
        var sut = new OrganizationValidator();

        // Act
        var result = sut.Validate(organization);

        // Assert
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(Organization.OrganizationName));
    }
}
