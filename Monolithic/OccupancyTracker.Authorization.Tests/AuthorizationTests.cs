using System.Security;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.Models;
using OccupancyTracker.Service;

namespace OccupancyTracker.Authorization.Tests;

public class AuthorizationTests
{
    [Fact]
    public async Task ActorIsResolvedFromAuthenticatedSubjectNotCallerClaims()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.SetPrincipal(new Claim("sub", "auth0|alice"), new Claim("userInformationSqid", "user:2"),
            new Claim("IsSuperAdmin", "true"));

        await using var context = fixture.Factory.CreateDbContext();
        var storedUser = await context.UserInformation.SingleAsync(x => x.UserInformationId == 1);
        storedUser.IsSuperAdmin = true;
        await context.SaveChangesAsync();
        var actor = await fixture.Request.GetCurrentUserAsync();
        Assert.Equal("user:1", actor!.UserInformationSqid);
        Assert.False(actor.IsSuperAdmin);
        fixture.Accessor.HttpContext!.User.AddIdentity(new ClaimsIdentity(new[]
        {
            new Claim("https://occupancy-tracker.secure-scalable.solutions/roles", "SuperAdmin")
        }));
        Assert.False((await fixture.Request.GetCurrentUserAsync())!.IsSuperAdmin);

        fixture.SetPrincipal(new Claim("sub", "auth0|unknown"), new Claim("https://occupancy-tracker.secure-scalable.solutions/roles", "SuperAdmin"));
        await Assert.ThrowsAsync<SecurityException>(() => fixture.Request.GetCurrentUserAsync());
        fixture.SetPrincipal(new Claim("sub", "auth0|alice"), new Claim(ClaimTypes.NameIdentifier, "auth0|bob"));
        await Assert.ThrowsAsync<SecurityException>(() => fixture.Request.GetCurrentUserAsync());

        fixture.SetPrincipal(new Claim("sub", "auth0|alice"),
            new Claim("https://occupancy-tracker.secure-scalable.solutions/roles", "SuperAdmin"));
        Assert.Equal("SuperAdmin", await fixture.Request.GetLocationRoleAsync("org:2", "loc:2"));
        Assert.Equal("", await fixture.Request.GetLocationRoleAsync("org:1", "loc:2"));
    }

    [Fact]
    public async Task RolesAreScopedToActiveMembershipAndExactLocation()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.SetPrincipal(new Claim("sub", "auth0|alice"));

        Assert.Equal("LocationAdmin", await fixture.Request.GetLocationRoleAsync("org:1", "loc:1"));
        Assert.Equal("", await fixture.Request.GetLocationRoleAsync("org:1", "loc:2"));
        Assert.Equal("", await fixture.Request.GetLocationRoleAsync("org:2", "loc:2"));
        Assert.Equal("", await fixture.Legacy.GetUserInformationRoleForLocationAsync("user:1", "org:2", "loc:1"));
        Assert.False(await fixture.Legacy.HasAccessToLocationAsync("user:1", "loc:2"));

        await using var context = fixture.Factory.CreateDbContext();
        var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationUsersId == 1);
        membership.UserStatusId = AuthorizationRecords.UserStatus.NoAccess.Id;
        await context.SaveChangesAsync();
        Assert.Equal("", await fixture.Request.GetLocationRoleAsync("org:1", "loc:1"));
    }

    [Fact]
    public async Task CounterAccessRequiresCurrentActiveCounterAndHonorsAuthentication()
    {
        var fixture = await Fixture.CreateAsync();
        Assert.True(await fixture.Request.CanUseCounterAsync("public-counter"));
        Assert.False(await fixture.Request.CanUseCounterAsync("private-counter"));
        Assert.False(await fixture.Request.CanUseCounterAsync("replaced-counter"));

        fixture.SetPrincipal(new Claim("sub", "auth0|alice"));
        Assert.True(await fixture.Request.CanUseCounterAsync("private-counter"));

        await using var context = fixture.Factory.CreateDbContext();
        var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationUsersId == 1);
        membership.EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        await context.SaveChangesAsync();
        Assert.False(await fixture.Request.CanUseCounterAsync("private-counter"));
    }

    [Fact]
    public async Task ProfileSaveDoesNotAllowPrivilegeOrIdentityChanges()
    {
        var fixture = await Fixture.CreateAsync();
        var edited = await fixture.Legacy.GetAsync("user:1");
        edited.IsSuperAdmin = true;
        edited.UserInformationId = 2;
        edited.EmailAddress = "attacker@example.com";
        edited.FirstName = "Updated";
        edited.BelongsToOrganization = true;

        var saved = await fixture.Legacy.SaveUserAsync(edited, "user:1");
        Assert.Equal(1, saved.UserInformationId);
        Assert.Equal("alice@example.com", saved.EmailAddress);
        Assert.False(saved.IsSuperAdmin);
        Assert.False(saved.BelongsToOrganization);
        Assert.Equal("Updated", saved.FirstName);
    }

    [Fact]
    public async Task SsoLinkingRequiresVerifiedEmailAndUsesExistingSubjectLink()
    {
        var fixture = await Fixture.CreateAsync();
        var unverified = new AuthenticationState(Principal(new Claim(ClaimTypes.NameIdentifier, "auth0|new"),
            new Claim(ClaimTypes.Email, "alice@example.com")));
        await Assert.ThrowsAsync<SecurityException>(() => fixture.Legacy.GetFromStateAsync(unverified));

        var existing = new AuthenticationState(Principal(new Claim(ClaimTypes.NameIdentifier, "auth0|alice"),
            new Claim(ClaimTypes.Email, "bob@example.com")));
        var actor = await fixture.Legacy.GetFromStateAsync(existing);
        Assert.Equal("user:1", actor!.UserInformationSqid);

        var verified = new AuthenticationState(Principal(new Claim(ClaimTypes.NameIdentifier, "auth0|new"),
            new Claim(ClaimTypes.Email, "bob@example.com"), new Claim("email_verified", "true")));
        var linked = await fixture.Legacy.GetFromStateAsync(verified);
        Assert.Equal("user:2", linked!.UserInformationSqid);
    }

    [Fact]
    public void UnknownAndOutOfScopeRolesDoNotGrantAccess()
    {
        var roles = new[]
        {
            new CurrentUserRoleInformation { OrganizationSqid = "a", LocationSqid = "x", RoleName = "LocationAdmin" },
            new CurrentUserRoleInformation { OrganizationSqid = "b", OrganizationWide = true, RoleName = "No Access" },
            new CurrentUserRoleInformation { OrganizationSqid = "b", OrganizationWide = true, RoleName = "unrecognized" }
        };
        Assert.Equal("", ResourceAuthorization.OrganizationRole(roles, "b", false));
        Assert.Equal("", ResourceAuthorization.LocationRole(roles, "b", "x", false));
        Assert.Equal("", ResourceAuthorization.LocationRole(roles, "a", "y", false));
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

    private sealed class Fixture
    {
        public required ContextFactory Factory { get; init; }
        public required OccAuthorizationService Legacy { get; init; }
        public required RequestAuthorizationService Request { get; init; }
        public required TestHttpContextAccessor Accessor { get; init; }

        public void SetPrincipal(params Claim[] claims) => Accessor.HttpContext!.User = Principal(claims);

        public static async Task<Fixture> CreateAsync()
        {
            var factory = new ContextFactory();
            await using var db = factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            db.UserInformation.AddRange(
                new UserInformation { UserInformationId = 1, UserInformationSqid = "user:1", FirstName = "Alice", LastName = "A", EmailAddress = "alice@example.com" },
                new UserInformation { UserInformationId = 2, UserInformationSqid = "user:2", FirstName = "Bob", LastName = "B", EmailAddress = "bob@example.com" });
            db.UserSsoInformation.Add(new UserSsoInformation { UserSsoInformationId = 1, UserInformationId = 1, Auth0Identifier = "auth0|alice" });
            db.Organizations.AddRange(new Organization { OrganizationId = 1, OrganizationSqid = "org:1" },
                new Organization { OrganizationId = 2, OrganizationSqid = "org:2" });
            db.Locations.AddRange(new Location { LocationId = 1, OrganizationId = 1, LocationSqid = "loc:1" },
                new Location { LocationId = 2, OrganizationId = 2, LocationSqid = "loc:2" });
            db.OrganizationUsers.Add(new OrganizationUser { OrganizationUsersId = 1, OrganizationId = 1, UserInformationId = 1 });
            db.OrganizationUserRoles.Add(new OrganizationUserRole { OrganizationUserRoleId = 1, OrganizationUserId = 1, LocationId = 1, RoleName = "LocationAdmin" });
            db.Entrances.AddRange(
                new Entrance { EntranceId = 1, OrganizationId = 1, LocationId = 1, EntranceCounterInstanceSqid = "public-counter" },
                new Entrance { EntranceId = 2, OrganizationId = 1, LocationId = 1, EntranceCounterInstanceSqid = "private-counter" });
            db.EntranceCounters.AddRange(
                new EntranceCounter { EntranceCounterId = 1, EntranceId = 1, OrganizationId = 1, LocationId = 1, EntranceCounterSqid = "public-counter" },
                new EntranceCounter { EntranceCounterId = 2, EntranceId = 2, OrganizationId = 1, LocationId = 1, EntranceCounterSqid = "private-counter", RequiresAuthentication = true },
                new EntranceCounter { EntranceCounterId = 3, EntranceId = 1, OrganizationId = 1, LocationId = 1, EntranceCounterSqid = "replaced-counter" });
            await db.SaveChangesAsync();

            var sqids = new TestSqids();
            var legacy = new OccAuthorizationService(factory, sqids, null!);
            var accessor = new TestHttpContextAccessor { HttpContext = new DefaultHttpContext() };
            return new Fixture { Factory = factory, Legacy = legacy, Accessor = accessor,
                Request = new RequestAuthorizationService(accessor, factory, legacy, sqids) };
        }
    }

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class ContextFactory : IDbContextFactory<OccupancyContext>
    {
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<OccupancyContext> options;

        public ContextFactory()
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
        public string EncodeOrganizationId(long id) => $"org:{id}";
        public string EncodeUserInformation(long id) => $"user:{id}";
        public long DecodeUserInformation(string id) => long.Parse(id["user:".Length..]);
        public string EncodeLocationId(long organizationId, long locationId) => $"loc:{locationId}";
        public string EncodeEntranceId(long organizationId, long locationId, long entranceId) => throw new NotSupportedException();
        public string EncodeEntranceCounterId(long organizationId, long locationId, long entranceId, long entranceCounterId) => throw new NotSupportedException();
        public string EncodeInvalidSecurityAttempt(long id) => throw new NotSupportedException();
        public string EncodeInvitationId(long organizationId, long invitationId) => throw new NotSupportedException();
        public ParsedOrganizationSqids DecodeSqids(string? OrganizationSqid = null, string? LocationSqid = null,
            string? EntranceSqid = null, string? EntranceCounterSqid = null) => throw new NotSupportedException();
    }
}
