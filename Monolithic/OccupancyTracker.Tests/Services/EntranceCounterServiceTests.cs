using Enyim.Caching;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;
using OccupancyTracker.IService;
using OccupancyTracker.Models;
using OccupancyTracker.Service;
using Sqids;

namespace OccupancyTracker.Tests.Services;

public sealed class EntranceCounterServiceTests
{
    [Test]
    public async Task UpdateCountAsync_ExistingLocation_AppliesDeltaAndAddsLog()
    {
        // Arrange
        var factory = new TestContextFactory();
        await using var seed = factory.CreateDbContext();
        await seed.Database.EnsureCreatedAsync();
        seed.Locations.Add(new Location
        {
            LocationId = 1,
            OrganizationId = 1,
            LocationSqid = "loc:1",
            CurrentOccupancy = 10
        });
        await seed.SaveChangesAsync();
        var sut = new EntranceCounterService(
            factory,
            new TestSqids(),
            Mock.Of<IMemcachedClient>(),
            Mock.Of<IOccAuthorizationService>());

        // Act
        var result = await sut.UpdateCountAsync("counter:1", -3);

        // Assert
        result.Should().Be(7);
        await using var verify = factory.CreateDbContext();
        (await verify.OccupancyLogs.CountAsync()).Should().Be(1);
        (await verify.Locations.SingleAsync()).CurrentOccupancy.Should().Be(7);
    }

    [Test]
    public async Task UpdateCountAsync_MissingLocation_ReturnsZero()
    {
        // Arrange
        var factory = new TestContextFactory();
        await using var seed = factory.CreateDbContext();
        await seed.Database.EnsureCreatedAsync();
        var sut = new EntranceCounterService(
            factory,
            new TestSqids(),
            Mock.Of<IMemcachedClient>(),
            Mock.Of<IOccAuthorizationService>());

        // Act
        var result = await sut.UpdateCountAsync("counter:missing", 5);

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public async Task GetLocationSqidAsync_DecodedLocation_ReturnsLocationSqid()
    {
        // Arrange
        var factory = new TestContextFactory();
        var sut = new EntranceCounterService(
            factory,
            new TestSqids(),
            Mock.Of<IMemcachedClient>(),
            Mock.Of<IOccAuthorizationService>());

        // Act
        var result = await sut.GetLocationSqidAsync("counter:1");

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    private sealed class TestContextFactory : IDbContextFactory<OccupancyContext>
    {
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<OccupancyContext> options;

        public TestContextFactory()
        {
            connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            connection.CreateCollation("SQL_Latin1_General_CP1_CS_AS", string.CompareOrdinal);
            options = new DbContextOptionsBuilder<OccupancyContext>().UseSqlite(connection).Options;
        }

        public OccupancyContext CreateDbContext() => new(options);
    }

    private sealed class TestSqids : ISqidsEncoderFactory
    {
        public string EncodeOrganizationId(long organizationId) => $"org:{organizationId}";
        public string EncodeLocationId(long organizationId, long locationId) => $"loc:{locationId}";
        public string EncodeEntranceId(long organizationId, long locationId, long entranceId) => $"ent:{entranceId}";
        public string EncodeEntranceCounterId(long organizationId, long locationId, long entranceId, long entranceCounterId) => $"counter:{entranceCounterId}";
        public string EncodeUserInformation(long userInformationId) => $"user:{userInformationId}";
        public string EncodeInvalidSecurityAttempt(long invalidSecurityAttemptId) => invalidSecurityAttemptId.ToString();
        public long DecodeUserInformation(string userInformationSqid) => long.Parse(userInformationSqid.Split(':')[1]);
        public ParsedOrganizationSqids DecodeSqids(string? OrganizationSqid = null, string? LocationSqid = null, string? EntranceSqid = null, string? EntranceCounterSqid = null) =>
            new(new SqidsEncoder<long>())
            {
                OrganizationId = 1,
                LocationId = 1,
                EntranceId = 1,
                EntranceCounterId = 1
            };
        public string EncodeInvitationId(long organizationId, long invitationId) => $"invite:{organizationId}:{invitationId}";
    }
}
