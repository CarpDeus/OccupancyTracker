using System.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using OccupancyTracker.DTO;
using OccupancyTracker.IService;
using OccupancyTracker.IRepository;
using OccupancyTracker.Repository;
using OccupancyTracker.Models;
using OccupancyTracker.Service;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(services => SsoOptions.Load(
    services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IWebHostEnvironment>().IsDevelopment()));
builder.Services.AddDataProtection().SetApplicationName("OccupancyTracker.Api");
builder.Services.AddOptions<KeyManagementOptions>().Configure<IConfiguration, ILoggerFactory>((options, config, loggers) =>
{
    var keyPath = config["DataProtection:KeyPath"];
    if (string.IsNullOrWhiteSpace(keyPath))
        throw new InvalidOperationException("DataProtection:KeyPath must be configured to a persistent shared directory.");
    options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(keyPath), loggers);
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer()
    .AddCookie("external", options =>
    {
        options.Cookie.Name = builder.Environment.IsDevelopment() ? "OccupancyTracker.External" : "__Host-OccupancyTracker.External";
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddOpenIdConnect("oidc", _ => { });
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<SsoOptions>((options, sso) =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = sso.Issuer,
        ValidateAudience = true, ValidAudience = sso.Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(sso.SigningKey),
        ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
    };
});
builder.Services.AddOptions<OpenIdConnectOptions>("oidc").Configure<SsoOptions, IWebHostEnvironment>((options, sso, environment) =>
{
    options.SignInScheme = "external";
    options.Authority = sso.Authority;
    options.RequireHttpsMetadata = !environment.IsDevelopment();
    options.ClientId = sso.ClientId;
    options.ClientSecret = sso.ClientSecret;
    options.CallbackPath = sso.CallbackPath;
    options.ResponseType = OpenIdConnectResponseType.Code;
    options.UsePkce = true;
    options.MapInboundClaims = false;
    options.GetClaimsFromUserInfoEndpoint = true;
    options.Scope.Clear();
    foreach (var scope in sso.Scopes) options.Scope.Add(scope);
    options.ClaimActions.MapJsonKey("email", "email");
    options.ClaimActions.MapJsonKey("email_verified", "email_verified");
    options.ClaimActions.MapJsonKey("given_name", "given_name");
    options.ClaimActions.MapJsonKey("family_name", "family_name");
    options.ClaimActions.MapJsonKey("picture", "picture");
    options.ClaimActions.MapJsonKey(sso.GroupClaim, sso.GroupClaim);
    options.Events.OnRedirectToIdentityProvider = context =>
    {
        context.ProtocolMessage.RedirectUri = new Uri(sso.PublicOrigin, sso.CallbackPath).ToString();
        return Task.CompletedTask;
    };
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var address in builder.Configuration.GetSection("TrustedProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(System.Net.IPAddress.Parse(address));
});
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddEnyimMemcached(builder.Configuration.GetSection("enyimMemcached"));
builder.Services.AddDbContextFactory<OccupancyContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Occupancy")
        ?? throw new InvalidOperationException("Connection string 'Occupancy' not found.");
    options.UseSqlServer(connectionString);
}, ServiceLifetime.Transient);
builder.Services.AddSingleton<ISqidsEncoderFactory, SqidsEncoderFactory>();
builder.Services.AddSingleton<IRepositorySessionFactory, RepositorySessionFactory>();
builder.Services.AddSingleton<IDatabaseMigrator, DatabaseMigrator>();
builder.Services.AddSingleton<ICacheRepository, CacheRepository>();
builder.Services.AddSingleton<IOccAuthorizationService, OccAuthorizationService>();
builder.Services.AddScoped<RequestAuthorizationService>();
builder.Services.AddScoped<TrackerOperations>();
builder.Services.AddScoped<OrganizationOperations>();
builder.Services.AddScoped<FacilityOperations>();
builder.Services.AddScoped<OrganizationUserOperations>();
builder.Services.AddScoped<AdminProfileOperations>();
builder.Services.AddScoped<IAdminUtilsService, AdminUtilsService>();
builder.Services.AddScoped<SsoService>();
builder.Services.AddScoped<PublicContentService>();
builder.Services.AddScoped<OrganizationListingService>();
builder.Services.AddTransient<OccupancyDbInitializer>();
builder.Services.AddSingleton<ISendGridFactory, SendGridFactory>();
if (builder.Configuration.GetValue("DatabaseInitialization:Enabled", true))
    builder.Services.AddHostedService<DatabaseInitializationService>();
if (builder.Configuration.GetValue("BackgroundProcessors:Enabled", true))
{
    builder.Services.AddHostedService<OccupancyLogProcessor>();
    builder.Services.AddHostedService<OccupancyEmailProcessor>();
}

var app = builder.Build();
_ = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<JwtBearerOptions>>()
    .Get(JwtBearerDefaults.AuthenticationScheme);
if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("Occupancy")))
    throw new InvalidOperationException("Connection string 'Occupancy' not found.");
app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseEnyimMemcached();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/public-content/{page}", async (string page, PublicContentService contentService) =>
{
    var content = await contentService.GetAsync(page);
    return content == null ? Results.NotFound() : Results.Ok(content);
});

app.MapGet("/api/auth/status", (SsoOptions options) =>
    Results.Ok(new { provider = "OIDC", signInUrl = "/api/auth/sign-in" }));

app.MapGet("/api/auth/sign-in", (string? challenge) =>
{
    if (challenge == null || !SsoService.ValidSecret(challenge)) return Results.BadRequest();
    var properties = new AuthenticationProperties { RedirectUri = "/api/auth/callback" };
    properties.Items["challenge"] = challenge;
    return Results.Challenge(properties, ["oidc"]);
});

app.MapGet("/api/auth/callback", async (HttpContext context, SsoService service, SsoOptions options, ILoggerFactory loggers) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    var external = await context.AuthenticateAsync("external");
    await context.SignOutAsync("external");
    if (!external.Succeeded || external.Principal == null ||
        !external.Properties!.Items.TryGetValue("challenge", out var challenge) ||
        challenge == null || !SsoService.ValidSecret(challenge))
        return Results.Unauthorized();
    try
    {
        var code = await service.CreateExchangeAsync(external.Principal, challenge);
        return Results.Redirect(Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(options.UiCallback.ToString(), "code", code));
    }
    catch (SecurityException exception)
    {
        loggers.CreateLogger("OidcSignIn").LogWarning(
            "OIDC account resolution rejected ({TraceId}): {Reason}", context.TraceIdentifier, exception.Message);
        return Results.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Sign-in could not be completed.",
            detail: "The identity provider authenticated you, but account resolution was rejected. Ask the administrator to check the API sign-in logs.",
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
    }
});

app.MapPost("/api/auth/exchange", async (SsoExchangeRequest request, SsoService service, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var result = await service.ExchangeAsync(request.Code ?? "", request.Verifier ?? "");
    return result == null ? Results.BadRequest() : Results.Ok(result);
});

app.MapGet("/api/me", async (RequestAuthorizationService authorization) =>
{
    try { return Results.Ok(await authorization.GetCurrentUserAsync()); }
    catch (SecurityException) { return Results.Forbid(); }
}).RequireAuthorization();

app.MapPut("/api/me", async (UserInformation edited, RequestAuthorizationService requestAuthorization, IOccAuthorizationService authorization) =>
{
    try
    {
        var actor = await requestAuthorization.GetCurrentUserAsync();
        if (actor == null || edited.UserInformationSqid != actor.UserInformationSqid)
            return Results.Forbid();
        if (edited.ContactAddress == null || edited.ContactPhoneNumber == null)
            return Results.BadRequest("Address and phone are required.");
        return Results.Ok(await authorization.SaveUserAsync(edited, actor.UserInformationSqid!));
    }
    catch (SecurityException) { return Results.Forbid(); }
}).RequireAuthorization();

app.MapGet("/api/admin/users/{sqid}", async (string sqid, AdminProfileOperations profiles) =>
    await ExecuteFacilityAsync(() => profiles.ExecuteAsync(sqid))).RequireAuthorization();
app.MapPut("/api/admin/users/{sqid}", async (string sqid, UserInformation edit, AdminProfileOperations profiles) =>
    await ExecuteFacilityAsync(() => profiles.ExecuteAsync(sqid, edit))).RequireAuthorization();

app.MapPost("/api/admin/cache/flush", async (RequestAuthorizationService authorization, IAdminUtilsService cache, ILoggerFactory loggerFactory) =>
{
    UserInformation? actor;
    try
    {
        actor = await authorization.GetCurrentUserAsync();
    }
    catch (SecurityException) { return Results.Forbid(); }
    if (actor?.IsSuperAdmin != true) return Results.Forbid();
    loggerFactory.CreateLogger("CacheAdministration").LogWarning(
        "Cache flush requested by {UserSqid}; completion on all cache servers cannot be verified.", actor.UserInformationSqid);
    await cache.ClearCacheAsync();
    return Results.Accepted();
}).RequireAuthorization();

app.MapGet("/api/organizations", async (OrganizationListingService organizations) =>
{
    try
    {
        var result = await organizations.ListAsync();
        return result == null ? Results.Forbid() : Results.Ok(result);
    }
    catch (SecurityException) { return Results.Forbid(); }
}).RequireAuthorization();

app.MapGet("/api/organizations/{sqid}", async (string sqid, OrganizationOperations organizations) =>
    await ExecuteOrganizationAsync(() => organizations.ReadAsync(sqid))).RequireAuthorization();
app.MapPost("/api/organizations", async (OrganizationEdit edit, OrganizationOperations organizations) =>
    await ExecuteOrganizationAsync(() => organizations.CreateAsync(edit))).RequireAuthorization();
app.MapPut("/api/organizations/{sqid}", async (string sqid, OrganizationEdit edit, OrganizationOperations organizations) =>
    await ExecuteOrganizationAsync(() => organizations.UpdateAsync(sqid, edit))).RequireAuthorization();
app.MapPost("/api/organizations/{sqid}/status", async (string sqid, OrganizationStatusChange change, OrganizationOperations organizations) =>
    await ExecuteOrganizationAsync(() => organizations.ChangeStatusAsync(sqid, change))).RequireAuthorization();

var users = app.MapGroup("/api/organizations/{orgSqid}").RequireAuthorization();
users.MapGet("/users/can-manage", async (string orgSqid, OrganizationUserOperations operations) =>
    await ExecuteFacilityAsync(() => operations.CanManageAsync(orgSqid)));
users.MapGet("/users", async (string orgSqid, OrganizationUserOperations operations) =>
    await ExecuteFacilityAsync(() => operations.ListUsersAsync(orgSqid)));
users.MapGet("/users/{userSqid}", async (string orgSqid, string userSqid, OrganizationUserOperations operations) =>
    await ExecuteFacilityAsync(() => operations.ReadMembershipAsync(orgSqid, userSqid)));
users.MapPut("/users/{userSqid}", async (string orgSqid, string userSqid, MembershipEdit edit, OrganizationUserOperations operations) =>
    await ExecuteFacilityAsync(() => operations.UpdateMembershipAsync(orgSqid, userSqid, edit)));
users.MapGet("/invitations", async (string orgSqid, OrganizationUserOperations operations) =>
    await ExecuteFacilityAsync(() => operations.ListInvitationsAsync(orgSqid)));
users.MapPost("/invitations", async (string orgSqid, InviteOrganizationUser invite, OrganizationUserOperations operations) =>
    await ExecuteFacilityAsync(() => operations.InviteAsync(orgSqid, invite)));
app.MapPost("/api/me/invitations/redeem", async (RedeemOrganizationInvitation invitation, OrganizationUserOperations operations) =>
    await ExecuteFacilityAsync(() => operations.RedeemAsync(invitation))).RequireAuthorization();

var locations = app.MapGroup("/api/organizations/{orgSqid}/locations").RequireAuthorization();
locations.MapGet("/", async (string orgSqid, FacilityOperations facilities) =>
    await ExecuteFacilityAsync(() => facilities.ListLocationsAsync(orgSqid)));
locations.MapPost("/", async (string orgSqid, LocationEdit edit, FacilityOperations facilities) =>
    await ExecuteFacilityAsync(() => facilities.CreateLocationAsync(orgSqid, edit)));
locations.MapGet("/can-create", async (string orgSqid, FacilityOperations facilities) =>
    await ExecuteFacilityAsync(() => facilities.CanCreateLocationAsync(orgSqid)));
locations.MapGet("/{locationSqid}", async (string orgSqid, string locationSqid, FacilityOperations facilities) =>
    await ExecuteFacilityAsync(() => facilities.ReadLocationAsync(orgSqid, locationSqid)));
locations.MapPut("/{locationSqid}", async (string orgSqid, string locationSqid, LocationEdit edit, FacilityOperations facilities) =>
    await ExecuteFacilityAsync(() => facilities.UpdateLocationAsync(orgSqid, locationSqid, edit)));
locations.MapGet("/{locationSqid}/can-manage", async (string orgSqid, string locationSqid, FacilityOperations facilities) =>
    await ExecuteFacilityAsync(() => facilities.CanManageLocationAsync(orgSqid, locationSqid)));
locations.MapPost("/{locationSqid}/status", async (string orgSqid, string locationSqid, OrganizationStatusChange change, FacilityOperations facilities) =>
    await ExecuteFacilityAsync(() => facilities.ChangeLocationStatusAsync(orgSqid, locationSqid, change)));
locations.MapGet("/{locationSqid}/entrances", async (string orgSqid, string locationSqid, FacilityOperations facilities) =>
    await ExecuteFacilityAsync(() => facilities.ListEntrancesAsync(orgSqid, locationSqid)));
locations.MapPost("/{locationSqid}/entrances", async (string orgSqid, string locationSqid, EntranceEdit edit, FacilityOperations facilities) =>
    await ExecuteFacilityAsync(() => facilities.CreateEntranceAsync(orgSqid, locationSqid, edit)));
locations.MapGet("/{locationSqid}/entrances/{entranceSqid}", async (string orgSqid, string locationSqid, string entranceSqid, FacilityOperations facilities) =>
    await ExecuteFacilityAsync(() => facilities.ReadEntranceAsync(orgSqid, locationSqid, entranceSqid)));
locations.MapPut("/{locationSqid}/entrances/{entranceSqid}", async (string orgSqid, string locationSqid, string entranceSqid, EntranceEdit edit, FacilityOperations facilities) =>
    await ExecuteFacilityAsync(() => facilities.UpdateEntranceAsync(orgSqid, locationSqid, entranceSqid, edit)));
locations.MapPost("/{locationSqid}/entrances/{entranceSqid}/status", async (string orgSqid, string locationSqid, string entranceSqid, OrganizationStatusChange change, FacilityOperations facilities) =>
    await ExecuteFacilityAsync(() => facilities.ChangeEntranceStatusAsync(orgSqid, locationSqid, entranceSqid, change)));

app.MapGet("/api/trackers/{counterSqid}", async (string counterSqid, TrackerOperations tracker) =>
    await ExecuteAsync(() => tracker.ReadAsync(counterSqid)));
app.MapPost("/api/trackers/{counterSqid}/count", async (string counterSqid, CounterChange change, TrackerOperations tracker) =>
    await ExecuteAsync(() => tracker.UpdateAsync(counterSqid, change.Delta)));

app.Run();

static async Task<IResult> ExecuteFacilityAsync<T>(Func<Task<FacilityResult<T>>> operation)
{
    try
    {
        var result = await operation();
        return result.Access switch
        {
            OrganizationAccess.Success => Results.Ok(result.Value),
            OrganizationAccess.NotFound => Results.NotFound(),
            OrganizationAccess.Forbidden => Results.Forbid(),
            OrganizationAccess.Invalid => Results.BadRequest("Invalid data or status transition."),
            _ => Results.Conflict("The resource status has changed.")
        };
    }
    catch (SecurityException) { return Results.Forbid(); }
}

static async Task<IResult> ExecuteOrganizationAsync(Func<Task<OrganizationResult>> operation)
{
    try
    {
        var result = await operation();
        return result.Access switch
        {
            OrganizationAccess.Success => Results.Ok(result.Organization),
            OrganizationAccess.NotFound => Results.NotFound(),
            OrganizationAccess.Forbidden => Results.Forbid(),
            OrganizationAccess.Invalid => Results.BadRequest("Invalid organization data or status transition."),
            _ => Results.Conflict("The organization status has changed.")
        };
    }
    catch (SecurityException) { return Results.Forbid(); }
}

static async Task<IResult> ExecuteAsync(Func<Task<TrackerResult>> operation)
{
    try
    {
        var result = await operation();
        return result.Access switch
        {
            TrackerAccess.Success => Results.Ok(result.Counter),
            TrackerAccess.NotFound => Results.NotFound(),
            TrackerAccess.Unauthenticated => Results.Unauthorized(),
            _ => Results.Forbid()
        };
    }
    catch (SecurityException)
    {
        return Results.Forbid();
    }
    catch (OverflowException)
    {
        return Results.BadRequest("The requested change would exceed the supported occupancy range.");
    }
}

public sealed class CounterChange
{
    public required int Delta { get; init; }
}
public partial class Program;
