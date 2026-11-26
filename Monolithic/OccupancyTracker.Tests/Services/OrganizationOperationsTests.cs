using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using Sqids;
using OccupancyTracker.DTO;
using OccupancyTracker.IService;
using OccupancyTracker.Models;
using OccupancyTracker.Service;

namespace OccupancyTracker.Tests.Services;

public sealed class OrganizationOperationsTests
{
    [Test]
    public async Task ReadAsync_MissingOrganization_ReturnsNotFound()
    {
        // Arrange
        var factory = new TestContextFactory();
        await using var seed = factory.CreateDbContext();
        await seed.Database.EnsureCreatedAsync();
        seed.UserInformation.Add(new UserInformation
        {
            UserInformationId = 1,
            UserInformationSqid = "user:1",
            EmailAddress = "alice@example.com",
            FirstName = "Alice",
            LastName = "A",
            CurrentStatusId = Statuses.DataStatus.Active.Id
        });
        seed.UserSsoInformation.Add(new UserSsoInformation
        {
            UserSsoInformationId = 1,
            UserInformationId = 1,
            Auth0Identifier = "auth0|alice"
        });
        await seed.SaveChangesAsync();

        var accessor = new TestHttpContextAccessor();
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("sub", "auth0|alice") }, "test"))
        };
        var authorization = new RequestAuthorizationService(
            accessor,
            factory,
            Mock.Of<IOccAuthorizationService>(),
            new TestSqids());
        var sut = new OrganizationOperations(factory, authorization, new TestSqids());

        // Act
        var result = await sut.ReadAsync("org:missing");

        // Assert
        result.Access.Should().Be(OrganizationAccess.NotFound);
    }

    [Test]
    public async Task UpdateAsync_InvalidOrganizationEdit_ReturnsInvalidBeforeAuthorization()
    {
        // Arrange
        var factory = new TestContextFactory();
        await using var seed = factory.CreateDbContext();
        await seed.Database.EnsureCreatedAsync();
        var accessor = new TestHttpContextAccessor();
        var authorization = new RequestAuthorizationService(
            accessor,
            factory,
            Mock.Of<IOccAuthorizationService>(),
            new TestSqids());
        var sut = new OrganizationOperations(factory, authorization, new TestSqids());
        var invalidEdit = new OrganizationEdit
        {
            OrganizationName = "",
            OrganizationDescription = "Description",
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

        // Act
        var result = await sut.UpdateAsync("org:1", invalidEdit);

        // Assert
        result.Access.Should().Be(OrganizationAccess.Invalid);
    }

    [Test]
    public async Task ChangeStatusAsync_InvalidStatusChange_ReturnsInvalid()
    {
        // Arrange
        var factory = new TestContextFactory();
        await using var seed = factory.CreateDbContext();
        await seed.Database.EnsureCreatedAsync();
        var accessor = new TestHttpContextAccessor();
        var authorization = new RequestAuthorizationService(
            accessor,
            factory,
            Mock.Of<IOccAuthorizationService>(),
            new TestSqids());
        var sut = new OrganizationOperations(factory, authorization, new TestSqids());
        var invalidChange = new OrganizationStatusChange { FromStatus = -1, ToStatus = 1 };

        // Act
        var result = await sut.ChangeStatusAsync("org:1", invalidChange);

        // Assert
        result.Access.Should().Be(OrganizationAccess.Invalid);
    }

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
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
