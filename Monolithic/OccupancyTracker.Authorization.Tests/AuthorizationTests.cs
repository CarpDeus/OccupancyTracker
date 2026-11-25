using System.Security;
using System.Security.Claims;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using OccupancyTracker.IService;
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
    public async Task TrackerOperationsEnforceAccessAndRecordOnlyAuthorizedUpdates()
    {
        var fixture = await Fixture.CreateAsync();
        Assert.Equal(TrackerAccess.Unauthenticated, (await fixture.Tracker.UpdateAsync("private-counter", 10)).Access);
        Assert.Equal(TrackerAccess.NotFound, (await fixture.Tracker.UpdateAsync("replaced-counter", 10)).Access);
        var anonymous = await fixture.Tracker.UpdateAsync("public-counter", 3);
        Assert.Equal(TrackerAccess.Success, anonymous.Access);
        Assert.Equal(3, anonymous.Counter!.CurrentOccupancy);

        fixture.SetPrincipal(new Claim("sub", "auth0|alice"));
        var authorized = await fixture.Tracker.UpdateAsync("private-counter", -1);
        Assert.Equal(TrackerAccess.Success, authorized.Access);
        Assert.Equal(2, authorized.Counter!.CurrentOccupancy);

        await using var db = fixture.Factory.CreateDbContext();
        Assert.Equal(2, await db.OccupancyLogs.CountAsync());
        Assert.Equal(2, (await fixture.Tracker.ReadAsync("public-counter")).Counter!.CurrentOccupancy);
        Assert.Contains("20241229153831_InitialCreate", db.Database.GetMigrations());
    }

    [Fact]
    public async Task ApiValidatesBearerTokensAndEnforcesCounterAuthorization()
    {
        var fixture = await Fixture.CreateAsync();
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        const string issuer = "https://issuer.example/";
        const string audience = "https://api.example/";
        using var app = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Auth0:Domain"] = "issuer.example",
                    ["Auth0:Audience"] = audience,
                    ["ConnectionStrings:Occupancy"] = "Server=unused;Database=unused;TrustServerCertificate=True"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDbContextFactory<OccupancyContext>>();
                services.AddSingleton<IDbContextFactory<OccupancyContext>>(fixture.Factory);
                services.RemoveAll<ISqidsEncoderFactory>();
                services.AddSingleton<ISqidsEncoderFactory>(new TestSqids());
                services.RemoveAll<IOccAuthorizationService>();
                services.AddSingleton<IOccAuthorizationService>(fixture.Legacy);
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Configuration = new OpenIdConnectConfiguration { Issuer = issuer };
                    options.Configuration.SigningKeys.Add(key);
                    options.TokenValidationParameters.ValidIssuer = issuer;
                    options.TokenValidationParameters.ValidAudience = audience;
                    options.TokenValidationParameters.IssuerSigningKey = key;
                    options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
                });
            });
        });
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/trackers/public-counter")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/trackers/private-counter")).StatusCode);

        string Token(string subject, string tokenAudience, DateTime expiration) => new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(issuer, tokenAudience, new[] { new Claim("sub", subject) },
                expires: expiration, signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256)));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(5)));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/trackers/private-counter")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/trackers/private-counter/count", new { delta = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/trackers/private-counter/count", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/trackers/private-counter/count", new { delta = int.MaxValue })).StatusCode);
        await using (var context = fixture.Factory.CreateDbContext())
            Assert.Equal(1, await context.OccupancyLogs.CountAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|bob", audience, DateTime.UtcNow.AddMinutes(5)));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/trackers/private-counter")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", "wrong-audience", DateTime.UtcNow.AddMinutes(5)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/trackers/private-counter")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(-1)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/trackers/private-counter")).StatusCode);
        var wrongIssuer = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            "https://wrong.example/", audience, new[] { new Claim("sub", "auth0|alice") },
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256)));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", wrongIssuer);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/trackers/private-counter")).StatusCode);
        using var otherRsa = RSA.Create(2048);
        var wrongKey = new RsaSecurityKey(otherRsa) { KeyId = key.KeyId };
        var badSignature = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer, audience, new[] { new Claim("sub", "auth0|alice") },
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(wrongKey, SecurityAlgorithms.RsaSha256)));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", badSignature);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/trackers/private-counter")).StatusCode);
    }

    [Fact]
    public async Task TrackerClientSendsOnlyItsCurrentAccessTokenAndSurfacesFailures()
    {
        var handler = new TrackerHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        var tokens = new TokenProvider { AccessToken = "first-user" };
        var client = new TrackerApiClient(http, tokens);

        await client.UpdateAsync("counter", 3);
        Assert.Equal("Bearer first-user", handler.Authorization);
        Assert.Equal("{\"delta\":3}", handler.Content);
        tokens.AccessToken = null;
        handler.Status = HttpStatusCode.Unauthorized;
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("counter"));
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Null(handler.Authorization);
        handler.Status = HttpStatusCode.NotFound;
        Assert.Null(await client.GetAsync("counter"));
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
        public required TrackerOperations Tracker { get; init; }
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
            db.UserSsoInformation.AddRange(
                new UserSsoInformation { UserSsoInformationId = 1, UserInformationId = 1, Auth0Identifier = "auth0|alice" },
                new UserSsoInformation { UserSsoInformationId = 2, UserInformationId = 2, Auth0Identifier = "auth0|bob" });
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
            var request = new RequestAuthorizationService(accessor, factory, legacy, sqids);
            return new Fixture { Factory = factory, Legacy = legacy, Accessor = accessor,
                Request = request, Tracker = new TrackerOperations(factory, request, accessor) };
        }
    }

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class TrackerHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string? Authorization { get; private set; }
        public string? Content { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            Content = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status) { Content = JsonContent.Create(new { entranceCounterSqid = "counter" }) };
        }
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
