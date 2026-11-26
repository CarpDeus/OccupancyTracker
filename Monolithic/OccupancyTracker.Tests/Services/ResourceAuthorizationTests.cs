using FluentAssertions;
using NUnit.Framework;
using OccupancyTracker.Models;
using OccupancyTracker.Service;

namespace OccupancyTracker.Tests.Services;

public sealed class ResourceAuthorizationTests
{
    [Test]
    public void OrganizationRole_SuperAdmin_ReturnsSuperAdmin()
    {
        // Arrange
        var roles = Array.Empty<CurrentUserRoleInformation>();
        const string organizationSqid = "org:1";

        // Act
        var result = ResourceAuthorization.OrganizationRole(roles, organizationSqid, true);

        // Assert
        result.Should().Be(AuthorizationRecords.Roles.SuperAdmin.Name);
    }

    [Test]
    public void OrganizationRole_OrganizationWideAdmin_ReturnsOrganizationAdmin()
    {
        // Arrange
        var roles = new[]
        {
            new CurrentUserRoleInformation
            {
                OrganizationSqid = "org:1",
                OrganizationWide = true,
                RoleName = AuthorizationRecords.Roles.OrganizationAdmin.Name
            }
        };

        // Act
        var result = ResourceAuthorization.OrganizationRole(roles, "org:1", false);

        // Assert
        result.Should().Be(AuthorizationRecords.Roles.OrganizationAdmin.Name);
    }

    [Test]
    public void LocationRole_RoleAtDifferentLocation_ReturnsEmpty()
    {
        // Arrange
        var roles = new[]
        {
            new CurrentUserRoleInformation
            {
                OrganizationSqid = "org:1",
                LocationSqid = "loc:1",
                RoleName = AuthorizationRecords.Roles.LocationAdmin.Name
            }
        };

        // Act
        var result = ResourceAuthorization.LocationRole(roles, "org:1", "loc:2", false);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void LocationRole_UnrecognizedOrganizationWideRole_ReturnsEmpty()
    {
        // Arrange
        var roles = new[]
        {
            new CurrentUserRoleInformation
            {
                OrganizationSqid = "org:1",
                OrganizationWide = true,
                RoleName = "Unknown"
            }
        };

        // Act
        var result = ResourceAuthorization.LocationRole(roles, "org:1", "loc:1", false);

        // Assert
        result.Should().BeEmpty();
    }
}
