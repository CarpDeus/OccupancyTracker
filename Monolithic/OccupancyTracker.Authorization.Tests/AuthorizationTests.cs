using System.Security;
using System.Security.Claims;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using OccupancyTracker.DTO;
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
        var userInfo = new UserInfoHandler();
        var cache = new FakeAdminUtils();
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
                    ["OccupancyDomain"] = "occupancy.example",
                    ["ConnectionStrings:Occupancy"] = "Server=unused;Database=unused;TrustServerCertificate=True"
                }));
            builder.ConfigureServices(services =>
            {
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
                services.AddHttpClient("auth0-userinfo").ConfigurePrimaryHttpMessageHandler(() => userInfo);
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
        Assert.Equal("Privacy", await client.GetFromJsonAsync<string>("/api/public-content/privacy"));
        Assert.Equal("Fallback", await client.GetFromJsonAsync<string>("/api/public-content/terms"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/public-content/secret")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/organizations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/admin/cache/flush", null)).StatusCode);
        Assert.Equal(0, cache.RequestCount);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/organizations/org:1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode = "invite:1:1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/me/bootstrap", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/trackers/public-counter")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/trackers/private-counter")).StatusCode);

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
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/admin/cache/flush", null)).StatusCode);
        Assert.Equal(0, cache.RequestCount);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            (await context.UserInformation.SingleAsync(x => x.UserInformationId == 1)).IsSuperAdmin = false;
            await context.SaveChangesAsync();
        }
        var listed = await client.GetFromJsonAsync<List<Organization>>("/api/organizations");
        Assert.Single(listed!);
        Assert.Equal("org:1", listed![0].OrganizationSqid);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/organizations/org:1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/organizations/org:1/status", new { fromStatus = 0, toStatus = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/organizations/org:1", edit)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/organizations/missing")).StatusCode);
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
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync("/api/organizations/org:1/locations")).StatusCode);
        var locationList = (await client.GetFromJsonAsync<List<Location>>("/api/organizations/org:1/locations"))!;
        Assert.Single(locationList);
        Assert.Equal("loc:1", locationList[0].LocationSqid);
        Assert.False(await client.GetFromJsonAsync<bool>("/api/organizations/org:1/locations/can-create"));
        Assert.True(await client.GetFromJsonAsync<bool>("/api/organizations/org:1/locations/loc:1/can-manage"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/organizations/org:1/locations/loc:3")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/organizations/org:1/locations/loc:2")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/organizations/org:1/locations", locationEdit)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/organizations/org:1/locations/loc:1", new
        {
            locationName = "Location", locationDescription = "", maxOccupancy = 5, occupancyThresholdWarning = 5,
            locationAddress = edit.organizationAddress, phoneNumber = edit.phoneNumber
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/organizations/org:1/locations/loc:1", locationEdit)).StatusCode);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var location = await context.Locations.SingleAsync(x => x.LocationId == 1);
            Assert.Equal(1, location.OrganizationId);
            Assert.Equal(0, location.CurrentStatus);
            Assert.Equal(0, location.CurrentOccupancy);
            Assert.NotEqual("forged", location.CreatedBy);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/organizations/org:1/locations/loc:1/entrances/ent:10")).StatusCode);
        var entranceResponse = await client.PostAsJsonAsync("/api/organizations/org:1/locations/loc:1/entrances", entranceEdit);
        Assert.Equal(HttpStatusCode.OK, entranceResponse.StatusCode);
        var newEntrance = (await entranceResponse.Content.ReadFromJsonAsync<Entrance>())!;
        Assert.Equal(1, newEntrance.OrganizationId);
        Assert.Equal(1, newEntrance.LocationId);
        Assert.Equal(0, newEntrance.CurrentStatus);
        Assert.NotEqual("forged", newEntrance.EntranceCounterInstanceSqid);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var counter = await context.EntranceCounters.SingleAsync(x => x.EntranceCounterSqid == newEntrance.EntranceCounterInstanceSqid);
            Assert.Equal(newEntrance.EntranceId, counter.EntranceId);
            Assert.False(counter.RequiresAuthentication);
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/trackers/{newEntrance.EntranceCounterInstanceSqid}")).StatusCode);
        var entrancePath = $"/api/organizations/org:1/locations/loc:1/entrances/{newEntrance.EntranceSqid}";
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(entrancePath, entranceEdit)).StatusCode);
        var afterEdit = (await client.GetFromJsonAsync<Entrance>(entrancePath))!;
        Assert.Equal(newEntrance.EntranceCounterInstanceSqid, afterEdit.EntranceCounterInstanceSqid);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"{entrancePath}/status", new { fromStatus = 1, toStatus = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"{entrancePath}/status", new { fromStatus = 0, toStatus = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/trackers/{newEntrance.EntranceCounterInstanceSqid}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"{entrancePath}/status", new { fromStatus = 1, toStatus = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{entrancePath}/status", new { fromStatus = 0, toStatus = 2 })).StatusCode);
        userInfo.Subject = "auth0|bob";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/me/bootstrap", null)).StatusCode);
        userInfo.Subject = "auth0|alice";
        var bootstrap = await client.PostAsync("/api/me/bootstrap", null);
        Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);
        Assert.Equal("user:1", (await bootstrap.Content.ReadFromJsonAsync<UserInformation>())!.UserInformationSqid);
        Assert.Equal("Bearer", userInfo.Authorization?.Scheme);
        var profile = await client.GetFromJsonAsync<UserInformation>("/api/me");
        profile!.UserInformationSqid = "user:2";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/me", profile)).StatusCode);
        profile.UserInformationSqid = "user:1";
        profile.IsSuperAdmin = true;
        profile.UserInformationId = 2;
        profile.FirstName = "Changed";
        var savedProfile = await client.PutAsJsonAsync("/api/me", profile);
        Assert.Equal(HttpStatusCode.OK, savedProfile.StatusCode);
        var saved = await savedProfile.Content.ReadFromJsonAsync<UserInformation>();
        Assert.Equal(1, saved!.UserInformationId);
        Assert.False(saved.IsSuperAdmin);
        Assert.Equal("Changed", saved.FirstName);
        userInfo.Subject = "auth0|new";
        userInfo.EmailVerified = false;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|new", audience, DateTime.UtcNow.AddMinutes(5)));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/me/bootstrap", null)).StatusCode);
        await using (var context = fixture.Factory.CreateDbContext())
            Assert.False(await context.UserSsoInformation.AnyAsync(x => x.Auth0Identifier == "auth0|new"));
        userInfo.EmailVerified = true;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/me/bootstrap", null)).StatusCode);
        Assert.Equal("user:1", (await client.GetFromJsonAsync<UserInformation>("/api/me"))!.UserInformationSqid);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(5)));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/trackers/private-counter")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/trackers/private-counter/count", new { delta = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/trackers/private-counter/count", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/trackers/private-counter/count", new { delta = int.MaxValue })).StatusCode);
        await using (var context = fixture.Factory.CreateDbContext())
            Assert.Equal(1, await context.OccupancyLogs.CountAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|bob", audience, DateTime.UtcNow.AddMinutes(5)));
        Assert.Empty((await client.GetFromJsonAsync<List<Organization>>("/api/organizations"))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/organizations/org:1/locations/loc:1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/organizations/org:1/locations/loc:1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/organizations/org:1/locations/loc:1/entrances", entranceEdit)).StatusCode);
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

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(5)));
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationUsersId == 1);
            membership.EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            await context.SaveChangesAsync();
        }
        Assert.Empty((await client.GetFromJsonAsync<List<Organization>>("/api/organizations"))!);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationUsersId == 1);
            membership.EndDate = null;
            var organization = await context.Organizations.SingleAsync(x => x.OrganizationId == 1);
            organization.CurrentStatus = 1;
            await context.SaveChangesAsync();
        }
        Assert.Empty((await client.GetFromJsonAsync<List<Organization>>("/api/organizations"))!);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            context.OrganizationUserRoles.Add(new OrganizationUserRole
            {
                OrganizationUserId = 1, OrganizationWide = true, RoleName = "OrganizationAdmin"
            });
            await context.SaveChangesAsync();
        }
        Assert.Single((await client.GetFromJsonAsync<List<Organization>>("/api/organizations"))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/organizations/org:2")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/organizations/org:1")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/organizations/org:2", edit)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/organizations/org:1", edit)).StatusCode);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var updated = await context.Organizations.SingleAsync(x => x.OrganizationId == 1);
            Assert.Equal("Updated", updated.OrganizationName);
            Assert.False(updated.PaidClient);
            Assert.Equal(1, updated.CurrentStatus);
            Assert.NotEqual("forged", updated.CreatedBy);
            Assert.Equal("user:1", updated.ModifiedBy);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/organizations/org:1/status", new { fromStatus = 0, toStatus = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/organizations/org:1/status", new { fromStatus = 1, toStatus = 7 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/organizations/org:1/status", new { fromStatus = 1, toStatus = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/organizations/org:1/status", new { fromStatus = 2, toStatus = 1 })).StatusCode);
        var adminToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer, audience, new[] { new Claim("sub", "auth0|alice"),
                new Claim("https://occupancy-tracker.secure-scalable.solutions/roles", "SuperAdmin") },
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256)));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/admin/cache/flush", null)).StatusCode);
        Assert.Equal(1, cache.RequestCount);
        cache.Fail = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsync("/api/admin/cache/flush", null)).StatusCode);
        cache.Fail = false;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/organizations/org:1/status", new { fromStatus = 2, toStatus = 1 })).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|bob", audience, DateTime.UtcNow.AddMinutes(5)));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/organizations", edit)).StatusCode);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var bob = await context.UserInformation.SingleAsync(x => x.UserInformationId == 2);
            bob.HasCompletedRegistration = true;
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/organizations", new
        {
            organizationName = "", organizationDescription = "", organizationAddress = edit.organizationAddress, phoneNumber = edit.phoneNumber
        })).StatusCode);
        var createdResponse = await client.PostAsJsonAsync("/api/organizations", edit);
        Assert.Equal(HttpStatusCode.OK, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<Organization>())!;
        Assert.NotEqual(2, created.OrganizationId);
        Assert.False(created.PaidClient);
        Assert.Equal(0, created.CurrentStatus);
        Assert.Equal("user:2", created.CreatedBy);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/organizations/{created.OrganizationSqid}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/organizations", edit)).StatusCode);
        var createdLocationResponse = await client.PostAsJsonAsync($"/api/organizations/{created.OrganizationSqid}/locations", locationEdit);
        Assert.Equal(HttpStatusCode.OK, createdLocationResponse.StatusCode);
        Assert.True(await client.GetFromJsonAsync<bool>($"/api/organizations/{created.OrganizationSqid}/locations/can-create"));
        var createdLocation = (await createdLocationResponse.Content.ReadFromJsonAsync<Location>())!;
        Assert.Equal(created.OrganizationId, createdLocation.OrganizationId);
        Assert.Equal(0, createdLocation.CurrentOccupancy);
        Assert.Equal("user:2", createdLocation.CreatedBy);
        var createdLocationPath = $"/api/organizations/{created.OrganizationSqid}/locations/{createdLocation.LocationSqid}";
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"{createdLocationPath}/status", new { fromStatus = 0, toStatus = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"{createdLocationPath}/entrances", entranceEdit)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"{createdLocationPath}/status", new { fromStatus = 1, toStatus = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"{createdLocationPath}/status", new { fromStatus = 2, toStatus = 1 })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"{createdLocationPath}/status", new { fromStatus = 2, toStatus = 1 })).StatusCode);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationId == created.OrganizationId);
            Assert.Equal(2, membership.UserInformationId);
            Assert.True(await context.OrganizationUserRoles.AnyAsync(x => x.OrganizationUserId == membership.OrganizationUsersId &&
                x.OrganizationWide && x.RoleName == "OrganizationAdmin"));
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
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/organizations/org:1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/organizations/org:1/invitations", new { emailAddress = "bob@example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/users/user:1")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(5)));
        Assert.True(await client.GetFromJsonAsync<bool>("/api/organizations/org:1/users/can-manage"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/organizations/org:2/invitations")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/organizations/org:1/invitations", new { emailAddress = "Invalid <bob@example.com>" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/organizations/org:1/invitations", new { emailAddress = " bob@example.com " })).StatusCode);
        var invitations = (await client.GetFromJsonAsync<List<OrganizationInvitationSummary>>("/api/organizations/org:1/invitations"))!;
        var invitationCode = Assert.Single(invitations).InvitationCode;
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var mail = JsonSerializer.Deserialize<SendGridData>((await context.EmailProcessorQueue.SingleAsync()).EmailProcessorData)!;
            Assert.Equal("bob@example.com", mail.ToEmailAddress);
            Assert.Contains("&lt;script&gt;", mail.HtmlContent);
            Assert.DoesNotContain("<script>", mail.HtmlContent);
            Assert.Contains("https://occupancy.example/profile/edit?invitationCode=", mail.HtmlContent);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|bob", audience, DateTime.UtcNow.AddMinutes(5)));
        await using (var context = fixture.Factory.CreateDbContext())
        {
            (await context.UserInformation.SingleAsync(x => x.UserInformationId == 2)).HasCompletedRegistration = false;
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode })).StatusCode);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            (await context.UserInformation.SingleAsync(x => x.UserInformationId == 2)).HasCompletedRegistration = true;
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode })).StatusCode);
        Assert.False(await client.GetFromJsonAsync<bool>("/api/organizations/org:1/users/can-manage"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/organizations/org:1/users")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(5)));
        var members = (await client.GetFromJsonAsync<List<OrganizationUserSummary>>("/api/organizations/org:1/users"))!;
        Assert.Equal(2, members.Count);
        Assert.Contains(members, x => x.UserInformationSqid == "user:2");
        Assert.Empty((await client.GetFromJsonAsync<List<OrganizationInvitationSummary>>("/api/organizations/org:1/invitations"))!);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/organizations/org:1/invitations", new { emailAddress = "bob@example.com" })).StatusCode);
        invitationCode = Assert.Single((await client.GetFromJsonAsync<List<OrganizationInvitationSummary>>("/api/organizations/org:1/invitations"))!).InvitationCode;
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationId == 1 && x.UserInformationId == 2);
            membership.UserStatusId = AuthorizationRecords.UserStatus.NoAccess.Id;
            await context.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|bob", audience, DateTime.UtcNow.AddMinutes(5)));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode })).StatusCode);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationId == 1 && x.UserInformationId == 2);
            membership.UserStatusId = AuthorizationRecords.UserStatus.Approved.Id;
            membership.EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode })).StatusCode);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationId == 1 && x.UserInformationId == 2);
            membership.EndDate = null;
            var invite = await context.OrganizationInvitationCodes.SingleAsync(x => x.InvitationCode == invitationCode);
            invite.CurrentStatus = 1;
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode })).StatusCode);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var invite = await context.OrganizationInvitationCodes.SingleAsync(x => x.InvitationCode == invitationCode);
            invite.CurrentStatus = 0;
            (await context.Organizations.SingleAsync(x => x.OrganizationId == 1)).CurrentStatus = 1;
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode })).StatusCode);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            (await context.Organizations.SingleAsync(x => x.OrganizationId == 1)).CurrentStatus = 0;
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/me/invitations/redeem", new { invitationCode })).StatusCode);
        await using (var context = fixture.Factory.CreateDbContext())
            Assert.Equal(1, await context.OrganizationUsers.CountAsync(x => x.OrganizationId == 1 && x.UserInformationId == 2));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/users/user:2")).StatusCode);
        var adminEdit = new
        {
            firstName = "Updated", lastName = "Bob", emailAddress = "forged@example.com", isSuperAdmin = true,
            userInformationId = 1, userInformationSqid = "user:1", belongsToOrganization = false,
            contactAddress = edit.organizationAddress, contactPhoneNumber = edit.phoneNumber
        };
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/admin/users/user:2", adminEdit)).StatusCode);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var bob = await context.UserInformation.SingleAsync(x => x.UserInformationId == 2);
            Assert.Equal("Updated", bob.FirstName);
            Assert.Equal("bob@example.com", bob.EmailAddress);
            Assert.Equal("user:2", bob.UserInformationSqid);
            Assert.False(bob.IsSuperAdmin);
            Assert.True(bob.BelongsToOrganization);
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
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync(alicePath)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|bob", audience, DateTime.UtcNow.AddMinutes(5)));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(alicePath)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(5)));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/organizations/org:2/users/user:1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/organizations/org:1/users/missing")).StatusCode);
        var aliceMembership = (await client.GetFromJsonAsync<OrganizationMembership>(alicePath))!;
        var bobMembership = (await client.GetFromJsonAsync<OrganizationMembership>(bobPath))!;
        Assert.Equal("OrganizationAdmin", aliceMembership.OrganizationRole);
        Assert.Contains(aliceMembership.Locations, x => x.LocationSqid == "loc:1" && x.RoleName == "LocationAdmin");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "SuperAdmin"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "User", status: 2))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "User",
            start: new DateOnly(2030, 1, 2), end: new DateOnly(2030, 1, 1)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, null,
            locationRoles: new() { new MembershipRoleEdit { LocationSqid = "loc:2", RoleName = "User" } }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, null,
            locationRoles: new() { new MembershipRoleEdit { LocationSqid = "loc:1", RoleName = "OrganizationAdmin" } }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, null,
            locationRoles: new() { new MembershipRoleEdit { LocationSqid = "loc:1", RoleName = "User" },
                new MembershipRoleEdit { LocationSqid = "loc:1", RoleName = "LocationAdmin" } }))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, "User"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, "OrganizationAdmin",
            end: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10))))).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, null, status: 1))).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(5)));
        var promotedResponse = await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "OrganizationAdmin"));
        Assert.Equal(HttpStatusCode.OK, promotedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "User"))).StatusCode);
        bobMembership = (await promotedResponse.Content.ReadFromJsonAsync<OrganizationMembership>())!;
        await using (var context = fixture.Factory.CreateDbContext())
        {
            (await context.UserInformation.SingleAsync(x => x.UserInformationId == 2)).CurrentStatusId = 1;
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, "User"))).StatusCode);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            (await context.UserInformation.SingleAsync(x => x.UserInformationId == 2)).CurrentStatusId = 0;
            var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationId == 1 && x.UserInformationId == 2);
            membership.EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, "User"))).StatusCode);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var membership = await context.OrganizationUsers.SingleAsync(x => x.OrganizationId == 1 && x.UserInformationId == 2);
            membership.EndDate = null;
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, "User"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(bobPath)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|bob", audience, DateTime.UtcNow.AddMinutes(5)));
        aliceMembership = (await client.GetFromJsonAsync<OrganizationMembership>(alicePath))!;
        var restrict = MembershipChange(aliceMembership, null, locationRoles: new()
        {
            new MembershipRoleEdit { LocationSqid = "loc:1", RoleName = "User" }
        });
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(alicePath, restrict)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(5)));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/organizations/org:1/locations/loc:1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/organizations/org:1/locations/loc:3")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "User"))).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|bob", audience, DateTime.UtcNow.AddMinutes(5)));
        aliceMembership = (await client.GetFromJsonAsync<OrganizationMembership>(alicePath))!;
        var temporaryAdmin = await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, "OrganizationAdmin",
            end: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10))));
        Assert.Equal(HttpStatusCode.OK, temporaryAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "User"))).StatusCode);
        aliceMembership = (await temporaryAdmin.Content.ReadFromJsonAsync<OrganizationMembership>())!;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, "OrganizationAdmin"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "User", status: 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(alicePath)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("auth0|alice", audience, DateTime.UtcNow.AddMinutes(5)));
        bobMembership = (await client.GetFromJsonAsync<OrganizationMembership>(bobPath))!;
        Assert.Equal(1, bobMembership.UserStatusId);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "User"))).StatusCode);
        bobMembership = (await client.GetFromJsonAsync<OrganizationMembership>(bobPath))!;
        var tampered = new
        {
            version = bobMembership.Version, userStatusId = 0, organizationRole = "User",
            locationRoles = new[] { new { locationSqid = "loc:1", roleName = "LocationAdmin" } },
            userInformationId = 1, organizationId = 2, isSuperAdmin = true
        };
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(bobPath, tampered)).StatusCode);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var alice = await context.OrganizationUsers.SingleAsync(x => x.OrganizationId == 1 && x.UserInformationId == 1);
            Assert.True(await context.OrganizationUserRoles.AnyAsync(x => x.OrganizationUserId == alice.OrganizationUsersId &&
                x.OrganizationWide && x.RoleName == "OrganizationAdmin"));
            var bob = await context.OrganizationUsers.SingleAsync(x => x.OrganizationId == 1 && x.UserInformationId == 2);
            Assert.True(await context.OrganizationUserRoles.AnyAsync(x => x.OrganizationUserId == bob.OrganizationUsersId &&
                x.LocationId == 1 && x.RoleName == "LocationAdmin"));
            Assert.False((await context.UserInformation.SingleAsync(x => x.UserInformationId == 2)).IsSuperAdmin);
        }
        bobMembership = (await client.GetFromJsonAsync<OrganizationMembership>(bobPath))!;
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(bobPath, MembershipChange(bobMembership, "OrganizationAdmin", start: tomorrow))).StatusCode);
        aliceMembership = (await client.GetFromJsonAsync<OrganizationMembership>(alicePath))!;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, "User"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(alicePath, MembershipChange(aliceMembership, "OrganizationAdmin",
            end: tomorrow.AddDays(-1)))).StatusCode);
        await using (var context = fixture.Factory.CreateDbContext())
        {
            var bob = await context.OrganizationUsers.SingleAsync(x => x.OrganizationId == 1 && x.UserInformationId == 2);
            context.OrganizationUserRoles.Add(new OrganizationUserRole
            {
                OrganizationUserId = bob.OrganizationUsersId, OrganizationWide = true, RoleName = "User"
            });
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync(bobPath)).StatusCode);
    }

    [Fact]
    public async Task AdminClientSendsCurrentTokenAndSurfacesFailure()
    {
        var handler = new TrackerHandler { Status = HttpStatusCode.Accepted };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        var tokens = new TokenProvider { AccessToken = "first-user" };
        var client = new AdminApiClient(http, tokens);
        await client.RequestCacheFlushAsync();
        Assert.Equal("/api/admin/cache/flush", handler.Path);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("Bearer first-user", handler.Authorization);
        tokens.AccessToken = "second-user";
        handler.Status = HttpStatusCode.Forbidden;
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.RequestCacheFlushAsync());
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Equal("Bearer second-user", handler.Authorization);
        handler.Status = HttpStatusCode.OK;
        await Assert.ThrowsAsync<HttpRequestException>(() => client.RequestCacheFlushAsync());
    }

    [Fact]
    public void UiRuntimeDoesNotDependOnBackendOrStorage()
    {
        Assert.DoesNotContain(typeof(OccupancyTracker.Program).Assembly.GetReferencedAssemblies(),
            x => x.Name == "OccupancyTracker.Backend");
        var deps = Path.Combine(AppContext.BaseDirectory, "OccupancyTracker.deps.json");
        using var json = JsonDocument.Parse(File.ReadAllText(deps));
        var targets = json.RootElement.GetProperty("targets");
        foreach (var target in targets.EnumerateObject())
        {
            var dependencies = target.Value.EnumerateObject().Select(x => x.Name).ToList();
            Assert.DoesNotContain(dependencies, x => x.StartsWith("OccupancyTracker.Backend/", StringComparison.Ordinal));
            Assert.DoesNotContain(dependencies, x => x.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
            Assert.DoesNotContain(dependencies, x => x.StartsWith("EnyimMemcachedCore/", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task UserManagementClientSendsCurrentTokenAndSurfacesFailures()
    {
        var handler = new TrackerHandler { ResponseBody = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        var tokens = new TokenProvider { AccessToken = "first-user" };
        var client = new OrganizationUserApiClient(http, tokens);
        Assert.True(await client.InviteAsync("org:1", "bob@example.com"));
        Assert.Equal("/api/organizations/org%3A1/invitations", handler.Path);
        Assert.Equal("Bearer first-user", handler.Authorization);
        using var body = JsonDocument.Parse(handler.Content!);
        Assert.Equal("bob@example.com", body.RootElement.GetProperty("emailAddress").GetString());
        tokens.AccessToken = "second-user";
        handler.Status = HttpStatusCode.Forbidden;
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.RedeemAsync("code"));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Equal("Bearer second-user", handler.Authorization);
        Assert.Equal("/api/me/invitations/redeem", handler.Path);
        handler.ResponseBody = new OrganizationMembership
        {
            UserInformationSqid = "user:1", Name = "Alice", EmailAddress = "alice@example.com",
            Locations = new(), Version = "version"
        };
        handler.Status = HttpStatusCode.OK;
        var membership = await client.GetMembershipAsync("org:1", "user:1");
        Assert.Equal("/api/organizations/org%3A1/users/user%3A1", handler.Path);
        await client.SaveMembershipAsync("org:1", "user:1", new MembershipEdit
        {
            Version = membership.Version, UserStatusId = 1, OrganizationRole = "User", LocationRoles = new()
        });
        Assert.Equal(HttpMethod.Put, handler.Method);
        using var edit = JsonDocument.Parse(handler.Content!);
        Assert.Equal("version", edit.RootElement.GetProperty("version").GetString());
        Assert.Equal(1, edit.RootElement.GetProperty("userStatusId").GetInt32());
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
    public async Task FacilityClientSendsScopedPathsAndOnlyEditableFields()
    {
        var handler = new TrackerHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        var tokens = new TokenProvider { AccessToken = "access-token" };
        var client = new FacilityApiClient(http, tokens);
        await client.SaveLocationAsync("org:1", new Location
        {
            LocationId = 7, LocationSqid = "loc:7", LocationName = "Name", OrganizationId = 2,
            CurrentStatus = 2, CurrentOccupancy = 42
        });
        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.Equal("/api/organizations/org%3A1/locations/loc%3A7", handler.Path);
        Assert.Equal("Bearer access-token", handler.Authorization);
        using var body = JsonDocument.Parse(handler.Content!);
        Assert.Equal("Name", body.RootElement.GetProperty("locationName").GetString());
        Assert.False(body.RootElement.TryGetProperty("organizationId", out _));
        Assert.False(body.RootElement.TryGetProperty("currentOccupancy", out _));
        Assert.False(body.RootElement.TryGetProperty("currentStatus", out _));
        handler.Status = HttpStatusCode.Forbidden;
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.ListEntrancesAsync("org:1", "loc:7"));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
    }

    [Fact]
    public async Task OrganizationClientSendsOnlyEditableFieldsAndCurrentToken()
    {
        var handler = new TrackerHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        var tokens = new TokenProvider { AccessToken = "first-user" };
        var client = new OrganizationApiClient(http, tokens);
        await client.SaveAsync(new Organization
        {
            OrganizationId = 7, OrganizationSqid = "org:7", OrganizationName = "Name",
            PaidClient = true, CurrentStatus = 2, CreatedBy = "forged"
        });
        Assert.Equal("Bearer first-user", handler.Authorization);
        Assert.Equal(HttpMethod.Put, handler.Method);
        using var body = JsonDocument.Parse(handler.Content!);
        Assert.Equal("Name", body.RootElement.GetProperty("organizationName").GetString());
        Assert.False(body.RootElement.TryGetProperty("paidClient", out _));
        Assert.False(body.RootElement.TryGetProperty("currentStatus", out _));
        Assert.False(body.RootElement.TryGetProperty("createdBy", out _));
        tokens.AccessToken = "second-user";
        handler.Status = HttpStatusCode.Forbidden;
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("org:7"));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Equal("Bearer second-user", handler.Authorization);
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

    private sealed class UserInfoHandler : HttpMessageHandler
    {
        public string Subject { get; set; } = "auth0|alice";
        public bool EmailVerified { get; set; } = true;
        public AuthenticationHeaderValue? Authorization { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { sub = Subject, email = "alice@example.com", email_verified = EmailVerified })
            });
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
        public string EncodeEntranceId(long organizationId, long locationId, long entranceId) => $"ent:{entranceId}";
        public string EncodeEntranceCounterId(long organizationId, long locationId, long entranceId, long entranceCounterId) => $"counter:{entranceCounterId}";
        public string EncodeInvalidSecurityAttempt(long id) => throw new NotSupportedException();
        public string EncodeInvitationId(long organizationId, long invitationId) => $"invite:{organizationId}:{invitationId}";
        public ParsedOrganizationSqids DecodeSqids(string? OrganizationSqid = null, string? LocationSqid = null,
            string? EntranceSqid = null, string? EntranceCounterSqid = null) => throw new NotSupportedException();
    }
}
