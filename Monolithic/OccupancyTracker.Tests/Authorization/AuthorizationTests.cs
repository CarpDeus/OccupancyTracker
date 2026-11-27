using System.Security;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using System.Security.Claims;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using OccupancyTracker.DTO;
using OccupancyTracker.IService;
using OccupancyTracker.IRepository;
using OccupancyTracker.Repository;
using OccupancyTracker.Models;
using OccupancyTracker.Service;

namespace OccupancyTracker.Tests.Authorization;

public class AuthorizationTests
{
    [Test]
    public async Task GetCurrentUserAsync_AuthenticatedSubjectAndCallerClaims_UsesStoredActorIdentity()
    {
        // Arrange
        var fixture = await Fixture.CreateAsync();
        fixture.SetPrincipal(new Claim("sub", "auth0|alice"), new Claim("userInformationSqid", "user:2"),
            new Claim("IsSuperAdmin", "true"));

        await using var context = fixture.Factory.CreateDbContext();
        var storedUser = await context.UserInformation.SingleAsync(x => x.UserInformationId == 1);
        storedUser.IsSuperAdmin = true;
        await context.SaveChangesAsync();
        // Act
        var actor = await fixture.Request.GetCurrentUserAsync();
        // Assert
        actor!.UserInformationSqid.Should().Be("user:1");
        actor.IsSuperAdmin.Should().BeFalse();
        fixture.Accessor.HttpContext!.User.AddIdentity(new ClaimsIdentity(new[]
        {
            new Claim("https://occupancy-tracker.secure-scalable.solutions/roles", "SuperAdmin")
        }));
        (await fixture.Request.GetCurrentUserAsync())!.IsSuperAdmin.Should().BeFalse();

        fixture.SetPrincipal(new Claim("sub", "auth0|unknown"), new Claim("https://occupancy-tracker.secure-scalable.solutions/roles", "SuperAdmin"));
        Func<Task> getUnknownCurrentUser = () => fixture.Request.GetCurrentUserAsync();
        await getUnknownCurrentUser.Should().ThrowAsync<SecurityException>();
        fixture.SetPrincipal(new Claim("sub", "auth0|alice"), new Claim(ClaimTypes.NameIdentifier, "auth0|bob"));
        Func<Task> getCurrentUser = () => fixture.Request.GetCurrentUserAsync();
        await getCurrentUser.Should().ThrowAsync<SecurityException>();

        fixture.SetPrincipal(new Claim("sub", "auth0|alice"),
            new Claim("https://occupancy-tracker.secure-scalable.solutions/roles", "SuperAdmin"));
        (await fixture.Request.GetLocationRoleAsync("org:2", "loc:2")).Should().Be("SuperAdmin");
        (await fixture.Request.GetLocationRoleAsync("org:1", "loc:2")).Should().Be("");
    }

    [Test]
    public async Task GetLocationRoleAsync_ActiveMembershipAndExactLocation_ReturnsScopedRole()
    {
        // Arrange
        var fixture = await Fixture.CreateAsync();
        fixture.SetPrincipal(new Claim("sub", "auth0|alice"));

        (await fixture.Request.GetLocationRoleAsync("org:1", "loc:1")).Should().Be("LocationAdmin");
        (await fixture.Request.GetLocationRoleAsync("org:1", "loc:2")).Should().Be("");
        (await fixture.Request.GetLocationRoleAsync("org:2", "loc:2")).Should().Be("");
        (await fixture.Legacy.GetUserInformationRoleForLocationAsync("user:1", "org:2", "loc:1")).Should().Be("");
        (await fixture.Legacy.HasAccessToLocationAsync("user:1", "loc:2")).Should().BeFalse();

        await using var context = fixture.Factory.CreateDbContext();
        var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationUsersId == 1);
        membership.UserStatusId = AuthorizationRecords.UserStatus.NoAccess.Id;
        await context.SaveChangesAsync();
        (await fixture.Request.GetLocationRoleAsync("org:1", "loc:1")).Should().Be("");
    }

    [Test]
    public async Task CanUseCounterAsync_CurrentActiveCounterAndAuthentication_ReturnsExpectedAccess()
    {
        // Arrange
        var fixture = await Fixture.CreateAsync();
        (await fixture.Request.CanUseCounterAsync("public-counter")).Should().BeTrue();
        (await fixture.Request.CanUseCounterAsync("private-counter")).Should().BeFalse();
        (await fixture.Request.CanUseCounterAsync("replaced-counter")).Should().BeFalse();

        fixture.SetPrincipal(new Claim("sub", "auth0|alice"));
        (await fixture.Request.CanUseCounterAsync("private-counter")).Should().BeTrue();

        await using var context = fixture.Factory.CreateDbContext();
        var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationUsersId == 1);
        membership.EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        await context.SaveChangesAsync();
        (await fixture.Request.CanUseCounterAsync("private-counter")).Should().BeFalse();
    }

    [Test]
    public async Task UpdateAsync_AuthorizedAndAnonymousCounters_RecordsOnlyAuthorizedUpdates()
    {
        // Arrange
        var fixture = await Fixture.CreateAsync();
        (await fixture.Tracker.UpdateAsync("private-counter", 10)).Access.Should().Be(TrackerAccess.Unauthenticated);
        (await fixture.Tracker.UpdateAsync("replaced-counter", 10)).Access.Should().Be(TrackerAccess.NotFound);
        var anonymous = await fixture.Tracker.UpdateAsync("public-counter", 3);
        anonymous.Access.Should().Be(TrackerAccess.Success);
        anonymous.Counter!.CurrentOccupancy.Should().Be(3);

        fixture.SetPrincipal(new Claim("sub", "auth0|alice"));
        var authorized = await fixture.Tracker.UpdateAsync("private-counter", -1);
        authorized.Access.Should().Be(TrackerAccess.Success);
        authorized.Counter!.CurrentOccupancy.Should().Be(2);

        await using var db = fixture.Factory.CreateDbContext();
        (await db.OccupancyLogs.CountAsync()).Should().Be(2);
        (await fixture.Tracker.ReadAsync("public-counter")).Counter!.CurrentOccupancy.Should().Be(2);
        db.Database.GetMigrations().Should().Contain("20241229153831_InitialCreate");
    }

    [Test]
    public async Task Api_ValidBearerAndAuthorizedCounter_EnforcesTokenAndResourceRules()
    {
        // Arrange
        var fixture = await Fixture.CreateAsync();
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        var cache = new FakeAdminUtils();
        const string issuer = "https://issuer.example/";
        const string audience = "https://api.example/";
        using var app = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Oidc:Authority"] = "https://issuer.example",
                    ["Oidc:ClientId"] = "test-client",
                    ["Oidc:ClientSecret"] = "test-secret",
                    ["Oidc:CallbackPath"] = "/signin-oidc",
                    ["Oidc:PublicOrigin"] = "https://api.example",
                    ["Oidc:UiCallback"] = "https://ui.example/sso-callback",
                    ["Oidc:SuperAdminGroup"] = "admins",
                    ["Jwt:Issuer"] = issuer,
                    ["Jwt:Audience"] = audience,
                    ["Jwt:SigningKey"] = Convert.ToBase64String(new byte[32]),
                    ["DataProtection:KeyPath"] = Path.Combine(Path.GetTempPath(), "occupancy-test-keys"),
                    ["OccupancyDomain"] = "occupancy.example",
                    ["ConnectionStrings:Occupancy"] = "Server=unused;Database=unused;TrustServerCertificate=True"
                }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) &&
                    (d.ImplementationType == typeof(DatabaseInitializationService) ||
                     d.ImplementationType == typeof(OccupancyLogProcessor) ||
                     d.ImplementationType == typeof(OccupancyEmailProcessor))).ToList())
                    services.Remove(descriptor);
                services.RemoveAll<IDbContextFactory<OccupancyContext>>();
                services.AddSingleton<IDbContextFactory<OccupancyContext>>(fixture.Factory);
                services.RemoveAll<ISqidsEncoderFactory>();
                services.AddSingleton<ISqidsEncoderFactory>(new TestSqids());
                services.RemoveAll<IOccAuthorizationService>();
                services.AddSingleton<IOccAuthorizationService>(fixture.Legacy);
                services.RemoveAll<IAdminUtilsService>();
                services.AddSingleton<IAdminUtilsService>(cache);
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters.ValidIssuer = issuer;
                    options.TokenValidationParameters.ValidAudience = audience;
                    options.TokenValidationParameters.IssuerSigningKey = key;
                    options.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.RsaSha256];
                    options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
                });
            });
        });
        using var client = app.CreateClient();
        await using (var context = fixture.Factory.CreateDbContext())
        {
            context.MarkdownText.AddRange(
                new MarkdownText { PageName = "privacy", TextIdentifier = "body", MarkdownContent = "Privacy" },
                new MarkdownText { PageName = "", TextIdentifier = "body", MarkdownContent = "Fallback" },
                new MarkdownText { PageName = "secret", TextIdentifier = "body", MarkdownContent = "Secret" },
                new MarkdownText { PageName = "terms", TextIdentifier = "body", MarkdownContent = "Inactive", CurrentStatus = 1 });
            context.Locations.Add(new Location { LocationId = 3, OrganizationId = 1, LocationSqid = "loc:3" });
            context.Entrances.Add(new Entrance { EntranceId = 10, OrganizationId = 2, LocationId = 2, EntranceSqid = "ent:10" });
            await context.SaveChangesAsync();
        }
        (await client.GetFromJsonAsync<string>("/api/public-content/privacy")).Should().Be("Privacy");
        (await client.GetFromJsonAsync<string>("/api/public-content/terms")).Should().Be("Fallback");
        (await client.GetAsync("/api/public-content/secret")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/organizations")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsync("/api/admin/cache/flush", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        cache.RequestCount.Should().Be(0);
        (await client.GetAsync("/api/organizations/org:1/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode = "invite:1:1" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/trackers/public-counter")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/trackers/private-counter")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        string Token(string subject, string tokenAudience, DateTime expiration) => new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(issuer, tokenAudience, new[] { new Claim("sub", subject) },
                expires: expiration, signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256)));

        var edit = new
        {
            organizationName = "Updated", organizationDescription = "Description",
            organizationAddress = new { addressLine1 = "123 Main", city = "City", state = "State", postalCode = "12345", country = "Country" },
            phoneNumber = new { countryCode = "1", number = "1234567" },
            organizationId = 2, organizationSqid = "org:2", paidClient = true, currentStatus = 0, createdBy = "forged"
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(5)));
        await using (var context = fixture.Factory.CreateDbContext())
        {
            (await context.UserInformation.SingleAsync(x => x.UserInformationId == 1)).IsSuperAdmin = true;
            await context.SaveChangesAsync();
        }
        (await client.PostAsync("/api/admin/cache/flush", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        cache.RequestCount.Should().Be(0);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            (await context.UserInformation.SingleAsync(x => x.UserInformationId == 1)).IsSuperAdmin = false;
            await context.SaveChangesAsync();
        }
        var listed = await client.GetFromJsonAsync<List<Organization>>("/api/organizations");
        listed!.Should().ContainSingle();
        listed![0].OrganizationSqid.Should().Be("org:1");
        (await client.GetAsync("/api/organizations/org:1")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/organizations/org:1/status", new { fromStatus = 0, toStatus = 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync("/api/organizations/org:1", edit)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/organizations/missing")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var locationEdit = new
        {
            locationName = "Location", locationDescription = "Description", maxOccupancy = 100, occupancyThresholdWarning = 80,
            locationAddress = edit.organizationAddress, phoneNumber = edit.phoneNumber,
            organizationId = 2, locationId = 2, currentOccupancy = 999, currentStatus = 2, createdBy = "forged"
        };
        var entranceEdit = new
        {
            entranceName = "Door", entranceDescription = "Front",
            locationId = 2, organizationId = 2, currentStatus = 2, entranceCounterInstanceSqid = "forged"
        };
        (await app.CreateClient().GetAsync("/api/organizations/org:1/locations")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var locationList = (await client.GetFromJsonAsync<List<Location>>("/api/organizations/org:1/locations"))!;
        locationList.Should().ContainSingle();
        locationList[0].LocationSqid.Should().Be("loc:1");
        (await client.GetFromJsonAsync<bool>("/api/organizations/org:1/locations/can-create")).Should().BeFalse();
        (await client.GetFromJsonAsync<bool>("/api/organizations/org:1/locations/loc:1/can-manage")).Should().BeTrue();
        (await client.GetAsync("/api/organizations/org:1/locations/loc:3")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/organizations/org:1/locations/loc:2")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/api/organizations/org:1/locations", locationEdit)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync("/api/organizations/org:1/locations/loc:1", new
        {
            locationName = "Location", locationDescription = "", maxOccupancy = 5, occupancyThresholdWarning = 5,
            locationAddress = edit.organizationAddress, phoneNumber = edit.phoneNumber
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync("/api/organizations/org:1/locations/loc:1", locationEdit)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var location = await context.Locations.SingleAsync(x => x.LocationId == 1);
            location.OrganizationId.Should().Be(1);
            location.CurrentStatus.Should().Be(0);
            location.CurrentOccupancy.Should().Be(0);
            location.CreatedBy.Should().NotBe("forged");
        }
        (await client.GetAsync("/api/organizations/org:1/locations/loc:1/entrances/ent:10")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var entranceResponse = await client.PostAsJsonAsync("/api/organizations/org:1/locations/loc:1/entrances", entranceEdit);
        entranceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var newEntrance = (await entranceResponse.Content.ReadFromJsonAsync<Entrance>())!;
        newEntrance.OrganizationId.Should().Be(1);
        newEntrance.LocationId.Should().Be(1);
        newEntrance.CurrentStatus.Should().Be(0);
        newEntrance.EntranceCounterInstanceSqid.Should().NotBe("forged");
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var counter = await context.EntranceCounters.SingleAsync(x => x.EntranceCounterSqid == newEntrance.EntranceCounterInstanceSqid);
            counter.EntranceId.Should().Be(newEntrance.EntranceId);
            counter.RequiresAuthentication.Should().BeFalse();
        }
        (await client.GetAsync($"/api/trackers/{newEntrance.EntranceCounterInstanceSqid}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var entrancePath = $"/api/organizations/org:1/locations/loc:1/entrances/{newEntrance.EntranceSqid}";
        (await client.PutAsJsonAsync(entrancePath, entranceEdit)).StatusCode.Should().Be(HttpStatusCode.OK);
        var afterEdit = (await client.GetFromJsonAsync<Entrance>(entrancePath))!;
        afterEdit.EntranceCounterInstanceSqid.Should().Be(newEntrance.EntranceCounterInstanceSqid);
        (await client.PostAsJsonAsync($"{entrancePath}/status", new { fromStatus = 1, toStatus = 0 })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync($"{entrancePath}/status", new { fromStatus = 0, toStatus = 1 })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/trackers/{newEntrance.EntranceCounterInstanceSqid}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"{entrancePath}/status", new { fromStatus = 1, toStatus = 0 })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync($"{entrancePath}/status", new { fromStatus = 0, toStatus = 2 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var profile = await client.GetFromJsonAsync<UserInformation>("/api/me");
        profile!.UserInformationSqid = "user:2";
        (await client.PutAsJsonAsync("/api/me", profile)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        profile.UserInformationSqid = "user:1";
        profile.IsSuperAdmin = true;
        profile.UserInformationId = 2;
        profile.FirstName = "Changed";
        var savedProfile = await client.PutAsJsonAsync("/api/me", profile);
        savedProfile.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await savedProfile.Content.ReadFromJsonAsync<UserInformation>();
        saved!.UserInformationId.Should().Be(1);
        saved.IsSuperAdmin.Should().BeFalse();
        saved.FirstName.Should().Be("Changed");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(5)));
        (await client.GetAsync("/api/trackers/private-counter")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/trackers/private-counter/count", new { delta = 2 })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/trackers/private-counter/count", new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/trackers/private-counter/count", new { delta = int.MaxValue })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using (var context = fixture.Factory.CreateDbContext())
            (await context.OccupancyLogs.CountAsync()).Should().Be(1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|bob", audience, DateTime.UtcNow.AddMinutes(5)));
        (await client.GetFromJsonAsync<List<Organization>>("/api/organizations"))!.Should().BeEmpty();
        (await client.GetAsync("/api/organizations/org:1/locations/loc:1")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/organizations/org:1/locations/loc:1")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/organizations/org:1/locations/loc:1/entrances", entranceEdit)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/trackers/private-counter")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", "wrong-audience", DateTime.UtcNow.AddMinutes(5)));
        (await client.GetAsync("/api/trackers/private-counter")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(-1)));
        (await client.GetAsync("/api/trackers/private-counter")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var wrongIssuer = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            "https://wrong.example/", audience, new[] { new Claim("sub", "auth0|alice") },
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256)));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", wrongIssuer);
        (await client.GetAsync("/api/trackers/private-counter")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var otherRsa = RSA.Create(2048);
        var wrongKey = new RsaSecurityKey(otherRsa) { KeyId = key.KeyId };
        var badSignature = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer, audience, new[] { new Claim("sub", "auth0|alice") },
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(wrongKey, SecurityAlgorithms.RsaSha256)));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", badSignature);
        (await client.GetAsync("/api/trackers/private-counter")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(5)));
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationUsersId == 1);
            membership.EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            await context.SaveChangesAsync();
        }
        (await client.GetFromJsonAsync<List<Organization>>("/api/organizations"))!.Should().BeEmpty();
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationUsersId == 1);
            membership.EndDate = null;
            var organization = await context.Organizations.SingleAsync(x => x.OrganizationId == 1);
            organization.CurrentStatus = 1;
            await context.SaveChangesAsync();
        }
        (await client.GetFromJsonAsync<List<Organization>>("/api/organizations"))!.Should().BeEmpty();
        await using (var context = fixture.Factory.CreateDbContext())
        {
            context.OrganizationUserRoles.Add(new OrganizationUserRole
            {
                OrganizationUserId = 1, OrganizationWide = true, RoleName = "OrganizationAdmin"
            });
            await context.SaveChangesAsync();
        }
        (await client.GetFromJsonAsync<List<Organization>>("/api/organizations"))!.Should().ContainSingle();
        (await client.GetAsync("/api/organizations/org:2")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/organizations/org:1")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PutAsJsonAsync("/api/organizations/org:2", edit)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync("/api/organizations/org:1", edit)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var updated = await context.Organizations.SingleAsync(x => x.OrganizationId == 1);
            updated.OrganizationName.Should().Be("Updated");
            updated.PaidClient.Should().BeFalse();
            updated.CurrentStatus.Should().Be(1);
            updated.CreatedBy.Should().NotBe("forged");
            updated.ModifiedBy.Should().Be("user:1");
        }
        (await client.PostAsJsonAsync("/api/organizations/org:1/status", new { fromStatus = 0, toStatus = 1 })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync("/api/organizations/org:1/status", new { fromStatus = 1, toStatus = 7 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/organizations/org:1/status", new { fromStatus = 1, toStatus = 2 })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/organizations/org:1/status", new { fromStatus = 2, toStatus = 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var adminToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer, audience, new[] { new Claim("sub", "auth0|alice"),
                new Claim("https://occupancy-tracker.secure-scalable.solutions/roles", "SuperAdmin") },
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256)));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        (await client.PostAsync("/api/admin/cache/flush", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        cache.RequestCount.Should().Be(1);
        cache.Fail = true;
        (await client.PostAsync("/api/admin/cache/flush", null)).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        cache.Fail = false;
        (await client.PostAsJsonAsync("/api/organizations/org:1/status", new { fromStatus = 2, toStatus = 1 })).StatusCode.Should().Be(HttpStatusCode.OK);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|bob", audience, DateTime.UtcNow.AddMinutes(5)));
        (await client.PostAsJsonAsync("/api/organizations", edit)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var bob = await context.UserInformation.SingleAsync(x => x.UserInformationId == 2);
            bob.HasCompletedRegistration = true;
            await context.SaveChangesAsync();
        }
        (await client.PostAsJsonAsync("/api/organizations", new
        {
            organizationName = "", organizationDescription = "", organizationAddress = edit.organizationAddress, phoneNumber = edit.phoneNumber
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var createdResponse = await client.PostAsJsonAsync("/api/organizations", edit);
        createdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = (await createdResponse.Content.ReadFromJsonAsync<Organization>())!;
        created.OrganizationId.Should().NotBe(2);
        created.PaidClient.Should().BeFalse();
        created.CurrentStatus.Should().Be(0);
        created.CreatedBy.Should().Be("user:2");
        (await client.GetAsync($"/api/organizations/{created.OrganizationSqid}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/organizations", edit)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var createdLocationResponse = await client.PostAsJsonAsync($"/api/organizations/{created.OrganizationSqid}/locations", locationEdit);
        createdLocationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<bool>($"/api/organizations/{created.OrganizationSqid}/locations/can-create")).Should().BeTrue();
        var createdLocation = (await createdLocationResponse.Content.ReadFromJsonAsync<Location>())!;
        createdLocation.OrganizationId.Should().Be(created.OrganizationId);
        createdLocation.CurrentOccupancy.Should().Be(0);
        createdLocation.CreatedBy.Should().Be("user:2");
        var createdLocationPath = $"/api/organizations/{created.OrganizationSqid}/locations/{createdLocation.LocationSqid}";
        (await client.PostAsJsonAsync($"{createdLocationPath}/status", new { fromStatus = 0, toStatus = 1 })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync($"{createdLocationPath}/entrances", entranceEdit)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync($"{createdLocationPath}/status", new { fromStatus = 1, toStatus = 2 })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync($"{createdLocationPath}/status", new { fromStatus = 2, toStatus = 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        (await client.PostAsJsonAsync($"{createdLocationPath}/status", new { fromStatus = 2, toStatus = 1 })).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationId == created.OrganizationId);
            membership.UserInformationId.Should().Be(2);
            (await context.OrganizationUserRoles.AnyAsync(x => x.OrganizationUserId == membership.OrganizationUsersId &&
                x.OrganizationWide && x.RoleName == "OrganizationAdmin")).Should().BeTrue();
        }

        await using (var context = fixture.Factory.CreateDbContext())
        {
            var org = await context.Organizations.SingleAsync(x => x.OrganizationId == 1);
            org.CurrentStatus = 0;
            org.OrganizationName = "<script>alert(1)</script>";
            var alice = await context.UserInformation.SingleAsync(x => x.UserInformationId == 1);
            alice.HasCompletedRegistration = true;
            await context.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|bob", audience, DateTime.UtcNow.AddMinutes(5)));
        (await client.GetAsync("/api/organizations/org:1/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/organizations/org:1/invitations", new { emailAddress = "bob@example.com" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/admin/users/user:1")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(5)));
        (await client.GetFromJsonAsync<bool>("/api/organizations/org:1/users/can-manage")).Should().BeTrue();
        (await client.GetAsync("/api/organizations/org:2/invitations")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/organizations/org:1/invitations", new { emailAddress = "Invalid <bob@example.com>" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/organizations/org:1/invitations", new { emailAddress = " bob@example.com " })).StatusCode.Should().Be(HttpStatusCode.OK);
        var invitations = (await client.GetFromJsonAsync<List<OrganizationInvitationSummary>>("/api/organizations/org:1/invitations"))!;
        var invitationCode = invitations.Should().ContainSingle().Which.InvitationCode;
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var mail = JsonSerializer.Deserialize<SendGridData>((await context.EmailProcessorQueue.SingleAsync()).EmailProcessorData)!;
            mail.ToEmailAddress.Should().Be("bob@example.com");
            mail.HtmlContent.Should().Contain("&lt;script&gt;");
            mail.HtmlContent.Should().NotContain("<script>");
            mail.HtmlContent.Should().Contain("https://occupancy.example/profile/edit?invitationCode=");
        }
        (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|bob", audience, DateTime.UtcNow.AddMinutes(5)));
        await using (var context = fixture.Factory.CreateDbContext())
        {
            (await context.UserInformation.SingleAsync(x => x.UserInformationId == 2)).HasCompletedRegistration = false;
            await context.SaveChangesAsync();
        }
        (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            (await context.UserInformation.SingleAsync(x => x.UserInformationId == 2)).HasCompletedRegistration = true;
            await context.SaveChangesAsync();
        }
        (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetFromJsonAsync<bool>("/api/organizations/org:1/users/can-manage")).Should().BeFalse();
        (await client.GetAsync("/api/organizations/org:1/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(5)));
        var members = (await client.GetFromJsonAsync<List<OrganizationUserSummary>>("/api/organizations/org:1/users"))!;
        members.Count.Should().Be(2);
        members.Should().Contain(x => x.UserInformationSqid == "user:2");
        (await client.GetFromJsonAsync<List<OrganizationInvitationSummary>>("/api/organizations/org:1/invitations"))!.Should().BeEmpty();
        (await client.PostAsJsonAsync("/api/organizations/org:1/invitations", new { emailAddress = "bob@example.com" })).StatusCode.Should().Be(HttpStatusCode.OK);
        invitationCode = (await client.GetFromJsonAsync<List<OrganizationInvitationSummary>>("/api/organizations/org:1/invitations"))!.Should().ContainSingle().Which.InvitationCode;
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationId == 1 && x.UserInformationId == 2);
            membership.UserStatusId = AuthorizationRecords.UserStatus.NoAccess.Id;
            await context.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|bob", audience, DateTime.UtcNow.AddMinutes(5)));
        (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationId == 1 && x.UserInformationId == 2);
            membership.UserStatusId = AuthorizationRecords.UserStatus.Approved.Id;
            membership.EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            await context.SaveChangesAsync();
        }
        (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationId == 1 && x.UserInformationId == 2);
            membership.EndDate = null;
            var invite = await context.OrganizationInvitationCodes.SingleAsync(x => x.InvitationCode == invitationCode);
            invite.CurrentStatus = 1;
            await context.SaveChangesAsync();
        }
        (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var invite = await context.OrganizationInvitationCodes.SingleAsync(x => x.InvitationCode == invitationCode);
            invite.CurrentStatus = 0;
            (await context.Organizations.SingleAsync(x => x.OrganizationId == 1)).CurrentStatus = 1;
            await context.SaveChangesAsync();
        }
        (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            (await context.Organizations.SingleAsync(x => x.OrganizationId == 1)).CurrentStatus = 0;
            await context.SaveChangesAsync();
        }
        (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode })).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var context = fixture.Factory.CreateDbContext())
            (await context.OrganizationUsers.CountAsync(x => x.OrganizationId == 1 && x.UserInformationId == 2)).Should().Be(1);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        (await client.GetAsync("/api/admin/users/user:2")).StatusCode.Should().Be(HttpStatusCode.OK);
        var adminEdit = new
        {
            firstName = "Updated", lastName = "Bob", emailAddress = "forged@example.com", isSuperAdmin = true,
            userInformationId = 1, userInformationSqid = "user:1", belongsToOrganization = false,
            contactAddress = edit.organizationAddress, contactPhoneNumber = edit.phoneNumber
        };
        (await client.PutAsJsonAsync("/api/admin/users/user:2", adminEdit)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var bob = await context.UserInformation.SingleAsync(x => x.UserInformationId == 2);
            bob.FirstName.Should().Be("Updated");
            bob.EmailAddress.Should().Be("bob@example.com");
            bob.UserInformationSqid.Should().Be("user:2");
            bob.IsSuperAdmin.Should().BeFalse();
            bob.BelongsToOrganization.Should().BeTrue();
        }

        static MembershipEdit MembershipChange(OrganizationMembership membership, string? role, int status = 0,
            DateOnly? start = null, DateOnly? end = null, List<MembershipRoleEdit>? locationRoles = null) => new()
        {
            Version = membership.Version, UserStatusId = status, StartDate = start, EndDate = end,
            OrganizationRole = role,
            LocationRoles = locationRoles ?? membership.Locations.Where(x => x.RoleName != null)
                .Select(x => new MembershipRoleEdit { LocationSqid = x.LocationSqid, RoleName = x.RoleName! }).ToList()
        };
        const string alicePath = "/api/organizations/org:1/users/user:1";
        const string bobPath = "/api/organizations/org:1/users/user:2";
        (await app.CreateClient().GetAsync(alicePath)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|bob", audience, DateTime.UtcNow.AddMinutes(5)));
        (await client.GetAsync(alicePath)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(5)));
        (await client.GetAsync("/api/organizations/org:2/users/user:1")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/organizations/org:1/users/missing")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var aliceMembership = (await client.GetFromJsonAsync<OrganizationMembership>(alicePath))!;
        var bobMembership = (await client.GetFromJsonAsync<OrganizationMembership>(bobPath))!;
        aliceMembership.OrganizationRole.Should().Be("OrganizationAdmin");
        aliceMembership.Locations.Should().Contain(x => x.LocationSqid == "loc:1" && x.RoleName == "LocationAdmin");
        (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "SuperAdmin"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "User", status: 2))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "User",
            start: new DateOnly(2030, 1, 2), end: new DateOnly(2030, 1, 1)))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, null,
            locationRoles: new() { new MembershipRoleEdit { LocationSqid = "loc:2", RoleName = "User" } }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, null,
            locationRoles: new() { new MembershipRoleEdit { LocationSqid = "loc:1", RoleName = "OrganizationAdmin" } }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, null,
            locationRoles: new() { new MembershipRoleEdit { LocationSqid = "loc:1", RoleName = "User" },
                new MembershipRoleEdit { LocationSqid = "loc:1", RoleName = "LocationAdmin" } }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, "User"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, "OrganizationAdmin",
            end: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10))))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        (await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, null, status: 1))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(5)));
        var promotedResponse = await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "OrganizationAdmin"));
        promotedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "User"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        bobMembership = (await promotedResponse.Content.ReadFromJsonAsync<OrganizationMembership>())!;
        await using (var context = fixture.Factory.CreateDbContext())
        {
            (await context.UserInformation.SingleAsync(x => x.UserInformationId == 2)).CurrentStatusId = 1;
            await context.SaveChangesAsync();
        }
        (await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, "User"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            (await context.UserInformation.SingleAsync(x => x.UserInformationId == 2)).CurrentStatusId = 0;
            var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationId == 1 && x.UserInformationId == 2);
            membership.EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            await context.SaveChangesAsync();
        }
        (await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, "User"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationId == 1 && x.UserInformationId == 2);
            membership.EndDate = null;
            await context.SaveChangesAsync();
        }
        (await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, "User"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(bobPath)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|bob", audience, DateTime.UtcNow.AddMinutes(5)));
        aliceMembership = (await client.GetFromJsonAsync<OrganizationMembership>(alicePath))!;
        var restrict = MembershipChange(aliceMembership, null, locationRoles: new()
        {
            new MembershipRoleEdit { LocationSqid = "loc:1", RoleName = "User" }
        });
        (await client.PutAsJsonAsync(alicePath, restrict)).StatusCode.Should().Be(HttpStatusCode.OK);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(5)));
        (await client.GetAsync("/api/organizations/org:1/locations/loc:1")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/organizations/org:1/locations/loc:3")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "User"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|bob", audience, DateTime.UtcNow.AddMinutes(5)));
        aliceMembership = (await client.GetFromJsonAsync<OrganizationMembership>(alicePath))!;
        var temporaryAdmin = await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, "OrganizationAdmin",
            end: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10))));
        temporaryAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "User"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        aliceMembership = (await temporaryAdmin.Content.ReadFromJsonAsync<OrganizationMembership>())!;
        (await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, "OrganizationAdmin"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "User", status: 1))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(alicePath)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(5)));
        bobMembership = (await client.GetFromJsonAsync<OrganizationMembership>(bobPath))!;
        bobMembership.UserStatusId.Should().Be(1);
        (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "User"))).StatusCode.Should().Be(HttpStatusCode.OK);
        bobMembership = (await client.GetFromJsonAsync<OrganizationMembership>(bobPath))!;
        var tampered = new
        {
            version = bobMembership.Version, userStatusId = 0, organizationRole = "User",
            locationRoles = new[] { new { locationSqid = "loc:1", roleName = "LocationAdmin" } },
            userInformationId = 1, organizationId = 2, isSuperAdmin = true
        };
        (await client.PutAsJsonAsync(bobPath, tampered)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var alice = await context.OrganizationUsers.SingleAsync(x => x.OrganizationId == 1 && x.UserInformationId == 1);
            (await context.OrganizationUserRoles.AnyAsync(x => x.OrganizationUserId == alice.OrganizationUsersId &&
                x.OrganizationWide && x.RoleName == "OrganizationAdmin")).Should().BeTrue();
            var bob = await context.OrganizationUsers.SingleAsync(x => x.OrganizationId == 1 && x.UserInformationId == 2);
            (await context.OrganizationUserRoles.AnyAsync(x => x.OrganizationUserId == bob.OrganizationUsersId &&
                x.LocationId == 1 && x.RoleName == "LocationAdmin")).Should().BeTrue();
            (await context.UserInformation.SingleAsync(x => x.UserInformationId == 2)).IsSuperAdmin.Should().BeFalse();
        }
        bobMembership = (await client.GetFromJsonAsync<OrganizationMembership>(bobPath))!;
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "OrganizationAdmin", start: tomorrow))).StatusCode.Should().Be(HttpStatusCode.OK);
        aliceMembership = (await client.GetFromJsonAsync<OrganizationMembership>(alicePath))!;
        (await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, "User"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, "OrganizationAdmin",
            end: tomorrow.AddDays(-1)))).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var bob = await context.OrganizationUsers.SingleAsync(x => x.OrganizationId == 1 && x.UserInformationId == 2);
            context.OrganizationUserRoles.Add(new OrganizationUserRole
            {
                OrganizationUserId = bob.OrganizationUsersId, OrganizationWide = true, RoleName = "User"
            });
            await context.SaveChangesAsync();
        }
        (await client.GetAsync(bobPath)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task RequestCacheFlushAsync_CurrentTokenAndFailure_SendsCurrentTokenAndSurfacesError()
    {
        // Arrange
        var handler = new TrackerHandler { Status = HttpStatusCode.Accepted };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        var tokens = new TokenProvider { AccessToken = "first-user" };
        var client = new AdminApiClient(http, tokens);
        await client.RequestCacheFlushAsync();
        handler.Path.Should().Be("/api/admin/cache/flush");
        handler.Method.Should().Be(HttpMethod.Post);
        handler.Authorization.Should().Be("Bearer first-user");
        tokens.AccessToken = "second-user";
        handler.Status = HttpStatusCode.Forbidden;
        Func<Task> flushCache = () => client.RequestCacheFlushAsync();
        var error = await flushCache.Should().ThrowAsync<HttpRequestException>();
        error.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        handler.Authorization.Should().Be("Bearer second-user");
        handler.Status = HttpStatusCode.OK;
        Func<Task> flushCacheFailure = () => client.RequestCacheFlushAsync();
        await flushCacheFailure.Should().ThrowAsync<HttpRequestException>();
    }

    [Test]
    public void UiAssemblyAndDependencies_NoBackendStorageReferences_ReturnsNoForbiddenDependency()
    {
        // Arrange
        typeof(OccupancyTracker.Program).Assembly.GetReferencedAssemblies().Should().NotContain(
            x => x.Name == "OccupancyTracker.Backend");
        var deps = Path.Combine(AppContext.BaseDirectory, "OccupancyTracker.deps.json");
        using var json = JsonDocument.Parse(File.ReadAllText(deps));
        var targets = json.RootElement.GetProperty("targets");
        foreach (var target in targets.EnumerateObject())
        {
            var dependencies = target.Value.EnumerateObject().Select(x => x.Name).ToList();
            dependencies.Should().NotContain(x => x.StartsWith("OccupancyTracker.Backend/", StringComparison.Ordinal));
            dependencies.Should().NotContain(x => x.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
            dependencies.Should().NotContain(x => x.StartsWith("EnyimMemcachedCore/", StringComparison.Ordinal));
        }
    }

    [Test]
    public async Task OrganizationUserClient_CurrentTokenAndFailures_SendsCurrentTokenAndSurfacesErrors()
    {
        // Arrange
        var handler = new TrackerHandler { ResponseBody = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        var tokens = new TokenProvider { AccessToken = "first-user" };
        var client = new OrganizationUserApiClient(http, tokens);
        (await client.InviteAsync("org:1", "bob@example.com")).Should().BeTrue();
        handler.Path.Should().Be("/api/organizations/org%3A1/invitations");
        handler.Authorization.Should().Be("Bearer first-user");
        using var body = JsonDocument.Parse(handler.Content!);
        body.RootElement.GetProperty("emailAddress").GetString().Should().Be("bob@example.com");
        tokens.AccessToken = "second-user";
        handler.Status = HttpStatusCode.Forbidden;
        Func<Task> redeem = () => client.RedeemAsync("code");
        var error = await redeem.Should().ThrowAsync<HttpRequestException>();
        error.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        handler.Authorization.Should().Be("Bearer second-user");
        handler.Path.Should().Be("/api/me/invitations/redeem");
        handler.ResponseBody = new OrganizationMembership
        {
            UserInformationSqid = "user:1", Name = "Alice", EmailAddress = "alice@example.com",
            Locations = new(), Version = "version"
        };
        handler.Status = HttpStatusCode.OK;
        var membership = await client.GetMembershipAsync("org:1", "user:1");
        handler.Path.Should().Be("/api/organizations/org%3A1/users/user%3A1");
        await client.SaveMembershipAsync("org:1", "user:1", new MembershipEdit
        {
            Version = membership.Version, UserStatusId = 1, OrganizationRole = "User", LocationRoles = new()
        });
        handler.Method.Should().Be(HttpMethod.Put);
        using var edit = JsonDocument.Parse(handler.Content!);
        edit.RootElement.GetProperty("version").GetString().Should().Be("version");
        edit.RootElement.GetProperty("userStatusId").GetInt32().Should().Be(1);
    }

    [Test]
    public async Task TrackerClient_CurrentAndMissingTokens_SendsOnlyCurrentTokenAndSurfacesErrors()
    {
        // Arrange
        var handler = new TrackerHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        var tokens = new TokenProvider { AccessToken = "first-user" };
        var client = new TrackerApiClient(http, tokens);

        await client.UpdateAsync("counter", 3);
        handler.Authorization.Should().Be("Bearer first-user");
        handler.Content.Should().Be("{\"delta\":3}");
        tokens.AccessToken = null;
        handler.Status = HttpStatusCode.Unauthorized;
        Func<Task> getCounter = () => client.GetAsync("counter");
        var exception = await getCounter.Should().ThrowAsync<HttpRequestException>();
        exception.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        handler.Authorization.Should().BeNull();
        handler.Status = HttpStatusCode.NotFound;
        (await client.GetAsync("counter")).Should().BeNull();
    }

    [Test]
    public async Task FacilityClient_ScopedPathsAndEditableFields_SendsOnlyAllowedData()
    {
        // Arrange
        var handler = new TrackerHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        var tokens = new TokenProvider { AccessToken = "access-token" };
        var client = new FacilityApiClient(http, tokens);
        await client.SaveLocationAsync("org:1", new Location
        {
            LocationId = 7, LocationSqid = "loc:7", LocationName = "Name", OrganizationId = 2,
            CurrentStatus = 2, CurrentOccupancy = 42
        });
        handler.Method.Should().Be(HttpMethod.Put);
        handler.Path.Should().Be("/api/organizations/org%3A1/locations/loc%3A7");
        handler.Authorization.Should().Be("Bearer access-token");
        using var body = JsonDocument.Parse(handler.Content!);
        body.RootElement.GetProperty("locationName").GetString().Should().Be("Name");
        body.RootElement.TryGetProperty("organizationId", out _).Should().BeFalse();
        body.RootElement.TryGetProperty("currentOccupancy", out _).Should().BeFalse();
        body.RootElement.TryGetProperty("currentStatus", out _).Should().BeFalse();
        handler.Status = HttpStatusCode.Forbidden;
        Func<Task> listEntrances = () => client.ListEntrancesAsync("org:1", "loc:7");
        var error = await listEntrances.Should().ThrowAsync<HttpRequestException>();
        error.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task OrganizationClient_EditableFieldsAndCurrentToken_SendsOnlyAllowedData()
    {
        // Arrange
        var handler = new TrackerHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        var tokens = new TokenProvider { AccessToken = "first-user" };
        var client = new OrganizationApiClient(http, tokens);
        await client.SaveAsync(new Organization
        {
            OrganizationId = 7, OrganizationSqid = "org:7", OrganizationName = "Name",
            PaidClient = true, CurrentStatus = 2, CreatedBy = "forged"
        });
        handler.Authorization.Should().Be("Bearer first-user");
        handler.Method.Should().Be(HttpMethod.Put);
        using var body = JsonDocument.Parse(handler.Content!);
        body.RootElement.GetProperty("organizationName").GetString().Should().Be("Name");
        body.RootElement.TryGetProperty("paidClient", out _).Should().BeFalse();
        body.RootElement.TryGetProperty("currentStatus", out _).Should().BeFalse();
        body.RootElement.TryGetProperty("createdBy", out _).Should().BeFalse();
        tokens.AccessToken = "second-user";
        handler.Status = HttpStatusCode.Forbidden;
        Func<Task> getOrganization = () => client.GetAsync("org:7");
        var error = await getOrganization.Should().ThrowAsync<HttpRequestException>();
        error.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        handler.Authorization.Should().Be("Bearer second-user");
    }

    [Test]
    public async Task SaveUserAsync_PrivilegeAndIdentityEdits_PreservesProtectedFields()
    {
        // Arrange
        var fixture = await Fixture.CreateAsync();
        var edited = await fixture.Legacy.GetAsync("user:1");
        edited.IsSuperAdmin = true;
        edited.UserInformationId = 2;
        edited.EmailAddress = "attacker@example.com";
        edited.FirstName = "Updated";
        edited.BelongsToOrganization = true;

        var saved = await fixture.Legacy.SaveUserAsync(edited, "user:1");
        saved.UserInformationId.Should().Be(1);
        saved.EmailAddress.Should().Be("alice@example.com");
        saved.IsSuperAdmin.Should().BeFalse();
        saved.BelongsToOrganization.Should().BeFalse();
        saved.FirstName.Should().Be("Updated");
    }

    [Test]
    public async Task GetFromStateAsync_VerifiedEmailAndExistingSubject_LinkReturnsExpectedUser()
    {
        // Arrange
        var fixture = await Fixture.CreateAsync();
        var unverified = new AuthenticationState(Principal(new Claim(ClaimTypes.NameIdentifier, "auth0|new"),
            new Claim(ClaimTypes.Email, "alice@example.com")));
        Func<Task> getFromState = () => fixture.Legacy.GetFromStateAsync(unverified);
        await getFromState.Should().ThrowAsync<SecurityException>();

        var existing = new AuthenticationState(Principal(new Claim(ClaimTypes.NameIdentifier, "auth0|alice"),
            new Claim(ClaimTypes.Email, "bob@example.com")));
        var actor = await fixture.Legacy.GetFromStateAsync(existing);
        actor!.UserInformationSqid.Should().Be("user:1");

        var verified = new AuthenticationState(Principal(new Claim(ClaimTypes.NameIdentifier, "auth0|new"),
            new Claim(ClaimTypes.Email, "bob@example.com"), new Claim("email_verified", "true")));
        var linked = await fixture.Legacy.GetFromStateAsync(verified);
        linked!.UserInformationSqid.Should().Be("user:2");
    }

    [Test]
    public async Task GetFromStateAsync_FirstUserIsNotSuperAdmin_AndRoleTracksCurrentClaims()
    {
        var factory = new ContextFactory();
        await using (var context = factory.CreateDbContext())
            await context.Database.EnsureCreatedAsync();
        var authorization = new OccAuthorizationService(factory, new TestSqids());

        var firstLogin = new AuthenticationState(Principal(
            new Claim(ClaimTypes.NameIdentifier, "auth0|first"),
            new Claim(ClaimTypes.Email, "first@example.com"),
            new Claim(ClaimTypes.GivenName, "First"),
            new Claim(ClaimTypes.Surname, "User"),
            new Claim("email_verified", "true")));
        var firstUser = await authorization.GetFromStateAsync(firstLogin);

        firstUser!.IsSuperAdmin.Should().BeFalse();

        var secondLogin = new AuthenticationState(Principal(
            new Claim(ClaimTypes.NameIdentifier, "auth0|second"),
            new Claim(ClaimTypes.Email, "second@example.com"),
            new Claim(ClaimTypes.GivenName, "Second"),
            new Claim(ClaimTypes.Surname, "User"),
            new Claim("email_verified", "true")));
        var secondUser = await authorization.GetFromStateAsync(secondLogin);

        secondUser!.IsSuperAdmin.Should().BeFalse();

        var adminLogin = new AuthenticationState(Principal(
            new Claim(ClaimTypes.NameIdentifier, "auth0|first"),
            new Claim(ClaimTypes.Email, "first@example.com"),
            new Claim("https://occupancy-tracker.secure-scalable.solutions/roles", "SuperAdmin"),
            new Claim("email_verified", "true")));
        (await authorization.GetFromStateAsync(adminLogin))!.IsSuperAdmin.Should().BeTrue();
        var returningFirstLogin = new AuthenticationState(Principal(
            new Claim(ClaimTypes.NameIdentifier, "auth0|first"),
            new Claim(ClaimTypes.Email, "first@example.com"),
            new Claim("email_verified", "true")));
        var returningFirstUser = await authorization.GetFromStateAsync(returningFirstLogin);
        returningFirstUser!.IsSuperAdmin.Should().BeFalse();
    }

    [Test]
    public async Task SsoExchange_VerifiedEmailLinksExistingUser_AndGroupsControlAdmin()
    {
        var fixture = await Fixture.CreateAsync();
        var options = TestSsoOptions("member_of");
        var protection = new EphemeralDataProtectionProvider();
        var firstReplica = new SsoService(options, fixture.Legacy, fixture.Factory, protection);
        var secondReplica = new SsoService(options, fixture.Legacy, fixture.Factory, protection);
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var principal = Principal(new Claim("sub", "provider-subject"), new Claim("email", "alice@example.com"),
            new Claim("email_verified", "true"), new Claim("member_of", "[\"staff\",\"admins\"]"));

        var code = await firstReplica.CreateExchangeAsync(principal, SsoOptions.Hash(verifier));
        (await secondReplica.ExchangeAsync(code, Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32)))).Should().BeNull();
        var result = await secondReplica.ExchangeAsync(code, verifier);
        result.Should().NotBeNull();
        (await firstReplica.ExchangeAsync(code, verifier)).Should().BeNull();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result!.AccessToken);
        jwt.Claims.Where(c => c.Type == "groups").Select(c => c.Value).Should().BeEquivalentTo("staff", "admins");
        jwt.Claims.Should().Contain(c => c.Type == "https://occupancy-tracker.secure-scalable.solutions/roles" && c.Value == "SuperAdmin");
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var linked = await context.UserSsoInformation.SingleAsync(x => x.Auth0Identifier == "oidc|" + SsoOptions.Hash(options.Authority + "\nprovider-subject"));
            linked.UserInformationId.Should().Be(1);
        }

        var withoutAdmin = Principal(new Claim("sub", "provider-subject"), new Claim("email", "alice@example.com"),
            new Claim("member_of", "staff"),
            new Claim("https://occupancy-tracker.secure-scalable.solutions/roles", "SuperAdmin"));
        var nextCode = await firstReplica.CreateExchangeAsync(withoutAdmin, SsoOptions.Hash(verifier));
        var next = await secondReplica.ExchangeAsync(nextCode, verifier);
        new JwtSecurityTokenHandler().ReadJwtToken(next!.AccessToken).Claims
            .Should().NotContain(c => c.Type == "https://occupancy-tracker.secure-scalable.solutions/roles");
        await using var db = fixture.Factory.CreateDbContext();
        (await db.UserInformation.SingleAsync(x => x.UserInformationId == 1)).IsSuperAdmin.Should().BeFalse();
    }

    [Test]
    public async Task SsoExchange_MappedSuperAdmin_AssumesEmailVerifiedButStillRequiresEmail()
    {
        var fixture = await Fixture.CreateAsync();
        var service = new SsoService(TestSsoOptions(), fixture.Legacy, fixture.Factory, new EphemeralDataProtectionProvider());
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        foreach (var emailVerified in new[] { "", "false" })
        {
            var principal = Principal(new Claim("sub", "admin-" + emailVerified),
                new Claim("email", "alice@example.com"), new Claim("groups", "admins"));
            if (emailVerified != "")
                principal.AddIdentity(new ClaimsIdentity([new Claim("email_verified", emailVerified)], "test"));
            var code = await service.CreateExchangeAsync(principal, SsoOptions.Hash(verifier));
            (await service.ExchangeAsync(code, verifier)).Should().NotBeNull();
        }

        var missingEmail = Principal(new Claim("sub", "admin-without-email"), new Claim("groups", "admins"));
        await FluentActions.Awaiting(() => service.CreateExchangeAsync(missingEmail, SsoOptions.Hash(verifier)))
            .Should().ThrowAsync<SecurityException>();
        var unmappedRole = Principal(new Claim("sub", "unmapped-admin"), new Claim("email", "alice@example.com"),
            new Claim("https://occupancy-tracker.secure-scalable.solutions/roles", "SuperAdmin"));
        await FluentActions.Awaiting(() => service.CreateExchangeAsync(unmappedRole, SsoOptions.Hash(verifier)))
            .Should().ThrowAsync<SecurityException>();
    }

    [Test]
    public async Task SsoExchange_VerificationDisabled_AllowsUnverifiedEmailButNotMissingEmail()
    {
        var fixture = await Fixture.CreateAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Oidc:Authority"] = "https://identity.example",
            ["Oidc:ClientId"] = "client", ["Oidc:ClientSecret"] = "secret",
            ["Oidc:CallbackPath"] = "/signin-oidc", ["Oidc:PublicOrigin"] = "https://api.example",
            ["Oidc:UiCallback"] = "https://ui.example/sso-callback", ["Oidc:SuperAdminGroup"] = "admins",
            ["Jwt:Issuer"] = "occupancy", ["Jwt:Audience"] = "api",
            ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }).Build();
        SsoOptions.Load(configuration, false).RequireVerifiedEmail.Should().BeTrue();
        configuration["Oidc:RequireVerifiedEmail"] = "false";
        var service = new SsoService(SsoOptions.Load(configuration, false), fixture.Legacy,
            fixture.Factory, new EphemeralDataProtectionProvider());
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        foreach (var verification in new[] { "", "false" })
        {
            var claims = new List<Claim>
            {
                new("sub", "unverified-" + verification), new("email", "alice@example.com")
            };
            if (verification != "") claims.Add(new Claim("email_verified", verification));
            var code = await service.CreateExchangeAsync(Principal(claims.ToArray()), SsoOptions.Hash(verifier));
            var session = await service.ExchangeAsync(code, verifier);
            session.Should().NotBeNull();
            new JwtSecurityTokenHandler().ReadJwtToken(session!.AccessToken).Claims
                .Should().NotContain(c => c.Type == "https://occupancy-tracker.secure-scalable.solutions/roles");
        }
        await using (var db = fixture.Factory.CreateDbContext())
            (await db.UserInformation.CountAsync()).Should().Be(2);
        await FluentActions.Awaiting(() => service.CreateExchangeAsync(
            Principal(new Claim("sub", "without-email")), SsoOptions.Hash(verifier)))
            .Should().ThrowAsync<SecurityException>();
    }

    [Test]
    public async Task SsoExchange_UnverifiedNewIdentityAndExpiredCode_AreRejected()
    {
        var fixture = await Fixture.CreateAsync();
        var service = new SsoService(TestSsoOptions(), fixture.Legacy, fixture.Factory, new EphemeralDataProtectionProvider());
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var unverified = Principal(new Claim("sub", "new"), new Claim("email", "alice@example.com"));
        await FluentActions.Awaiting(() => service.CreateExchangeAsync(unverified, SsoOptions.Hash(verifier)))
            .Should().ThrowAsync<SecurityException>();
        var verified = Principal(new Claim("sub", "new"), new Claim("email", "alice@example.com"), new Claim("email_verified", "true"));
        var code = await service.CreateExchangeAsync(verified, SsoOptions.Hash(verifier));
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var entry = await db.SsoExchangeCodes.SingleAsync();
            entry.ExpiresUtc = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        (await service.ExchangeAsync(code, verifier)).Should().BeNull();
    }

    [Test]
    public async Task Api_OidcCodeFlow_UsesUserInfoGroupsAndExchangesOnlyOnce()
    {
        var fixture = await Fixture.CreateAsync();
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "provider-key" };
        var provider = new OidcProviderHandler(key);
        const string issuer = "https://identity.example";
        const string audience = "occupancy-api";
        var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var app = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Oidc:Authority"] = issuer, ["Oidc:ClientId"] = "client",
                    ["Oidc:ClientSecret"] = "secret", ["Oidc:CallbackPath"] = "/signin-oidc",
                    ["Oidc:PublicOrigin"] = "https://localhost", ["Oidc:UiCallback"] = "https://ui.example/sso-callback",
                    ["Oidc:RequireVerifiedEmail"] = "true",
                    ["Oidc:SuperAdminGroup"] = "admins", ["Jwt:Issuer"] = "occupancy", ["Jwt:Audience"] = audience,
                    ["Jwt:SigningKey"] = signingKey, ["DataProtection:KeyPath"] = Path.Combine(Path.GetTempPath(), "occupancy-test-keys"),
                    ["ConnectionStrings:Occupancy"] = "Server=unused;Database=unused;TrustServerCertificate=True"
                }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) &&
                    (d.ImplementationType == typeof(DatabaseInitializationService) ||
                     d.ImplementationType == typeof(OccupancyLogProcessor) ||
                     d.ImplementationType == typeof(OccupancyEmailProcessor))).ToList())
                    services.Remove(descriptor);
                services.RemoveAll<IDbContextFactory<OccupancyContext>>();
                services.AddSingleton<IDbContextFactory<OccupancyContext>>(fixture.Factory);
                services.RemoveAll<IOccAuthorizationService>();
                services.AddSingleton<IOccAuthorizationService>(fixture.Legacy);
                services.PostConfigure<OpenIdConnectOptions>("oidc", options =>
                {
                    options.Configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = issuer, AuthorizationEndpoint = issuer + "/authorize",
                        TokenEndpoint = issuer + "/token", UserInfoEndpoint = issuer + "/userinfo"
                    };
                    options.Configuration.SigningKeys.Add(key);
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(options.Configuration);
                    options.Backchannel = new HttpClient(provider);
                });
            });
        });
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var start = await client.GetAsync("/api/auth/sign-in?challenge=" + SsoOptions.Hash(verifier));
        start.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var authorizationUrl = start.Headers.Location!;
        var parameters = QueryHelpers.ParseQuery(authorizationUrl.Query);
        parameters["redirect_uri"].ToString().Should().Be("https://localhost/signin-oidc");
        parameters["scope"].ToString().Should().Contain("groups");
        provider.Nonce = parameters["nonce"].ToString();
        var callback = await client.GetAsync("/signin-oidc?code=provider-code&state=" + Uri.EscapeDataString(parameters["state"].ToString()));
        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var complete = await client.GetAsync(callback.Headers.Location);
        complete.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var code = QueryHelpers.ParseQuery(complete.Headers.Location!.Query)["code"].ToString();
        var exchange = await client.PostAsJsonAsync("/api/auth/exchange", new { code, verifier });
        exchange.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await exchange.Content.ReadFromJsonAsync<SsoExchangeResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);
        (await client.GetFromJsonAsync<UserInformation>("/api/me"))!.IsSuperAdmin.Should().BeTrue();
        (await client.PostAsJsonAsync("/api/auth/exchange", new { code, verifier })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        provider.UserInfoSubject = "another-subject";
        var secondStart = await client.GetAsync("/api/auth/sign-in?challenge=" + SsoOptions.Hash(verifier));
        var secondParameters = QueryHelpers.ParseQuery(secondStart.Headers.Location!.Query);
        provider.Nonce = secondParameters["nonce"].ToString();
        var mismatched = await client.GetAsync("/signin-oidc?code=provider-code&state=" + Uri.EscapeDataString(secondParameters["state"].ToString()));
        mismatched.StatusCode.Should().NotBe(HttpStatusCode.Redirect);

        provider.Subject = provider.UserInfoSubject = "unlinked-subject";
        provider.EmailVerified = false;
        provider.Groups = ["staff"];
        var unverifiedStart = await client.GetAsync("/api/auth/sign-in?challenge=" + SsoOptions.Hash(verifier));
        var unverifiedParameters = QueryHelpers.ParseQuery(unverifiedStart.Headers.Location!.Query);
        provider.Nonce = unverifiedParameters["nonce"].ToString();
        var unverifiedCallback = await client.GetAsync("/signin-oidc?code=provider-code&state=" + Uri.EscapeDataString(unverifiedParameters["state"].ToString()));
        unverifiedCallback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var rejected = await client.GetAsync(unverifiedCallback.Headers.Location);
        rejected.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        rejected.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        await using var db = fixture.Factory.CreateDbContext();
        (await db.SsoExchangeCodes.CountAsync()).Should().Be(0);
    }

    [Test]
    public void SsoExchangeMigration_MatchesSqlServerModel()
    {
        var dbOptions = new DbContextOptionsBuilder<OccupancyContext>()
            .UseSqlServer("Server=unused;Database=unused;TrustServerCertificate=True").Options;
        using var db = new OccupancyContext(dbOptions);
        db.Database.HasPendingModelChanges().Should().BeFalse();
        db.GetService<IMigrator>().GenerateScript().Should().Contain("CREATE TABLE [SsoExchangeCodes]");
    }

    [Test]
    public async Task Ui_LoginBindsCallbackToBrowser_AndCreatesLocalSession()
    {
        var oldApi = Environment.GetEnvironmentVariable("Api__BaseAddress");
        var oldKeys = Environment.GetEnvironmentVariable("DataProtection__KeyPath");
        try
        {
            Environment.SetEnvironmentVariable("Api__BaseAddress", "https://api.example/");
            Environment.SetEnvironmentVariable("DataProtection__KeyPath", Path.Combine(Path.GetTempPath(), "occupancy-test-ui-keys"));
            var handler = new ExchangeHandler();
            using var app = new WebApplicationFactory<OccupancyTracker.Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                    services.AddHttpClient("sso").ConfigurePrimaryHttpMessageHandler(() => handler);
                });
            });
            using var client = app.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://ui.example"), AllowAutoRedirect = false, HandleCookies = true
            });
            (await client.GetAsync("/sso-callback?code=missing")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await client.GetAsync("/Account/Login?returnUrl=%2F%2Fevil.example")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var start = await client.GetAsync("/Account/Login?returnUrl=%2Forgs");
            start.StatusCode.Should().Be(HttpStatusCode.Redirect);
            start.Headers.Location!.Host.Should().Be("api.example");
            handler.Challenge = QueryHelpers.ParseQuery(start.Headers.Location.Query)["challenge"].ToString();
            var complete = await client.GetAsync("/sso-callback?code=valid");
            complete.StatusCode.Should().Be(HttpStatusCode.Redirect);
            complete.Headers.Location!.OriginalString.Should().Be("/orgs");
            complete.Headers.GetValues("Set-Cookie").Should().Contain(x => x.Contains(".AspNetCore.Cookies="));
            (await client.GetAsync("/sso-callback?code=valid")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Api__BaseAddress", oldApi);
            Environment.SetEnvironmentVariable("DataProtection__KeyPath", oldKeys);
        }
    }

    [TestCase("Development")]
    [TestCase("Production")]
    public async Task Ui_StaticAssets_ServesMudBlazorStylesAndScripts(string environment)
    {
        var oldApi = Environment.GetEnvironmentVariable("Api__BaseAddress");
        var oldKeys = Environment.GetEnvironmentVariable("DataProtection__KeyPath");
        try
        {
            Environment.SetEnvironmentVariable("Api__BaseAddress", "https://api.example/");
            Environment.SetEnvironmentVariable("DataProtection__KeyPath", Path.Combine(Path.GetTempPath(), "occupancy-test-ui-keys"));
            using var app = new WebApplicationFactory<OccupancyTracker.Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.ConfigureServices(services =>
                    services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider()));
            });
            using var client = app.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://ui.example"), AllowAutoRedirect = false
            });
            using var css = await client.GetAsync("/_content/MudBlazor/MudBlazor.min.css");
            css.StatusCode.Should().Be(HttpStatusCode.OK);
            css.Content.Headers.ContentType!.MediaType.Should().Be("text/css");
            (await css.Content.ReadAsStringAsync()).Should().Contain(".mud-");
            using var script = await client.GetAsync("/_content/MudBlazor/MudBlazor.min.js");
            script.StatusCode.Should().Be(HttpStatusCode.OK);
            (await script.Content.ReadAsStringAsync()).Should().Contain("mud");
        }
        finally
        {
            Environment.SetEnvironmentVariable("Api__BaseAddress", oldApi);
            Environment.SetEnvironmentVariable("DataProtection__KeyPath", oldKeys);
        }
    }

    [Test]
    public async Task Ui_MainAndTrackerPages_RenderMudThemeVariables()
    {
        var oldApi = Environment.GetEnvironmentVariable("Api__BaseAddress");
        var oldKeys = Environment.GetEnvironmentVariable("DataProtection__KeyPath");
        try
        {
            Environment.SetEnvironmentVariable("Api__BaseAddress", "https://api.example/");
            Environment.SetEnvironmentVariable("DataProtection__KeyPath", Path.Combine(Path.GetTempPath(), "occupancy-test-ui-keys"));
            using var app = new WebApplicationFactory<OccupancyTracker.Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                    services.AddHttpClient<TrackerApiClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => new TrackerHandler { Status = HttpStatusCode.NotFound });
                });
            });
            using var client = app.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://ui.example"), AllowAutoRedirect = false
            });
            foreach (var path in new[] { "/", "/trackers/missing" })
            {
                var html = await client.GetStringAsync(path);
                html.Should().Contain("--mud-appbar-height:")
                    .And.Contain("--mud-drawer-width-left:")
                    .And.Contain("--mud-palette-primary:");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("Api__BaseAddress", oldApi);
            Environment.SetEnvironmentVariable("DataProtection__KeyPath", oldKeys);
        }
    }

    private static SsoOptions TestSsoOptions(string groupClaim = "groups") => new()
    {
        Authority = "https://identity.example", ClientId = "client", ClientSecret = "secret",
        CallbackPath = "/signin-oidc", PublicOrigin = new Uri("https://api.example"), UiCallback = new Uri("https://ui.example/sso-callback"),
        GroupClaim = groupClaim, SuperAdminGroup = "admins", Scopes = ["openid", "email", "groups"],
        Issuer = "occupancy", Audience = "api", SigningKey = RandomNumberGenerator.GetBytes(32), ExpiryMinutes = 60
    };

    [Test]
    public void ResourceAuthorization_UnknownAndOutOfScopeRoles_ReturnsNoAccess()
    {
        // Arrange
        var roles = new[]
        {
            new CurrentUserRoleInformation { OrganizationSqid = "a", LocationSqid = "x", RoleName = "LocationAdmin" },
            new CurrentUserRoleInformation { OrganizationSqid = "b", OrganizationWide = true, RoleName = "No Access" },
            new CurrentUserRoleInformation { OrganizationSqid = "b", OrganizationWide = true, RoleName = "unrecognized" }
        };
        ResourceAuthorization.OrganizationRole(roles, "b", false).Should().Be("");
        ResourceAuthorization.LocationRole(roles, "b", "x", false).Should().Be("");
        ResourceAuthorization.LocationRole(roles, "a", "y", false).Should().Be("");
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
            var legacy = new OccAuthorizationService(factory, sqids);
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

    private sealed class OidcProviderHandler(RsaSecurityKey key) : HttpMessageHandler
    {
        public string Nonce { get; set; } = "";
        public string Subject { get; set; } = "provider-subject";
        public string UserInfoSubject { get; set; } = "provider-subject";
        public bool EmailVerified { get; set; } = true;
        public string[] Groups { get; set; } = ["staff", "admins"];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/token")
            {
                var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                    "https://identity.example", "client",
                    [new Claim("sub", Subject), new Claim("nonce", Nonce),
                        new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)],
                    expires: DateTime.UtcNow.AddMinutes(5),
                    signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256)));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { access_token = "provider-token", token_type = "Bearer", id_token = token })
                });
            }

            if (request.RequestUri.AbsolutePath == "/userinfo")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { sub = UserInfoSubject, email = "alice@example.com",
                        email_verified = EmailVerified, groups = Groups })
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class ExchangeHandler : HttpMessageHandler
    {
        public string Challenge { get; set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = await request.Content!.ReadFromJsonAsync<SsoExchangeRequest>(cancellationToken);
            if (request.RequestUri!.AbsolutePath != "/api/auth/exchange" || payload?.Code != "valid" ||
                SsoOptions.Hash(payload.Verifier) != Challenge)
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new SsoExchangeResponse("api-token", DateTime.UtcNow.AddMinutes(10), true))
            };
        }
    }

    private sealed class FakeAdminUtils : IAdminUtilsService
    {
        public int RequestCount { get; private set; }
        public bool Fail { get; set; }
        public Task ClearCacheAsync()
        {
            RequestCount++;
            if (Fail) throw new InvalidOperationException("Cache unavailable");
            return Task.CompletedTask;
        }
    }

    private sealed class TrackerHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public object ResponseBody { get; set; } = new { entranceCounterSqid = "counter" };
        public string? Authorization { get; private set; }
        public string? Content { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath;
            Content = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status) { Content = JsonContent.Create(ResponseBody) };
        }

    }

    private sealed class ContextFactory : IDbContextFactory<OccupancyContext>, IRepositorySessionFactory
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
        public IRepositorySession Create() => new RepositorySessionFactory(this).Create();
        public Task<IRepositorySession> CreateAsync(CancellationToken cancellationToken = default) =>
            new RepositorySessionFactory(this).CreateAsync(cancellationToken);
        public bool IsConcurrencyConflict(Exception exception) =>
            new RepositorySessionFactory(this).IsConcurrencyConflict(exception);
    }

    private sealed class TestSqids : ISqidsEncoderFactory
    {
        public string EncodeOrganizationId(long id) => $"org:{id}";
        public string EncodeUserInformation(long id) => $"user:{id}";
        public long DecodeUserInformation(string id) => long.Parse(id["user:".Length..]);
        public string EncodeLocationId(long organizationId, long locationId) => $"loc:{locationId}";
        public string EncodeEntranceId(long organizationId, long locationId, long entranceId) => $"ent:{entranceId}";
        public string EncodeEntranceCounterId(long organizationId, long locationId, long entranceId, long entranceCounterId) => $"counter:{entranceCounterId}";
        public string EncodeInvalidSecurityAttempt(long id) => throw new NotSupportedException();
        public string EncodeInvitationId(long organizationId, long invitationId) => $"invite:{organizationId}:{invitationId}";
        public ParsedOrganizationSqids DecodeSqids(string? OrganizationSqid = null, string? LocationSqid = null,
            string? EntranceSqid = null, string? EntranceCounterSqid = null) => throw new NotSupportedException();
    }
}
