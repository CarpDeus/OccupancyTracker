using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using OccupancyTracker.Service;

namespace OccupancyTracker.Tests.Clients;

public sealed class ApiAddressesTests
{
    [Test]
    public void Constructor_WithoutInternalAddress_UsesPublicAddressForRequests()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Api:BaseAddress"] = "https://api.example/prefix"
        }).Build();
        var expected = new Uri("https://api.example/prefix/");

        // Act
        var addresses = new ApiAddresses(configuration, false);

        // Assert
        addresses.Internal.Should().Be(expected);
    }

    [Test]
    public void Constructor_ExplicitInternalHttpAddress_PreservesPublicHttpsAddress()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Api:BaseAddress"] = "https://api.example/",
            ["Api:InternalBaseAddress"] = "http://api:8080"
        }).Build();
        var expected = new Uri("http://api:8080/");

        // Act
        var addresses = new ApiAddresses(configuration, false);

        // Assert
        addresses.Internal.Should().Be(expected);
        addresses.Public.Should().Be(new Uri("https://api.example/"));
    }

    [Test]
    public void Constructor_PublicHttpInProduction_RejectsAddress()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Api:BaseAddress"] = "http://api:8080/"
        }).Build();

        // Act
        Action construct = () => new ApiAddresses(configuration, false);

        // Assert
        construct.Should().Throw<InvalidOperationException>().WithMessage("Api:BaseAddress*");
    }

    [Test]
    public void Constructor_InvalidInternalAddress_RejectsInsteadOfUsingPublicFallback()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Api:BaseAddress"] = "https://api.example/",
            ["Api:InternalBaseAddress"] = "http://user:password@api:8080/"
        }).Build();

        // Act
        Action construct = () => new ApiAddresses(configuration, false);

        // Assert
        construct.Should().Throw<InvalidOperationException>().WithMessage("Api:InternalBaseAddress*");
    }
}
