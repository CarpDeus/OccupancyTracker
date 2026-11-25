using System.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.DTO;
using OccupancyTracker.IService;
using OccupancyTracker.Models;
using OccupancyTracker.Service;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    var domain = builder.Configuration["Auth0:Domain"];
    var audience = builder.Configuration["Auth0:Audience"];
    if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(audience))
        throw new InvalidOperationException("Auth0:Domain and Auth0:Audience must be configured.");
    options.Authority = new Uri($"https://{domain.TrimEnd('/')}/").ToString();
    options.Audience = audience;
    options.MapInboundClaims = false;
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
builder.Services.AddSingleton<IOccAuthorizationService, OccAuthorizationService>();
builder.Services.AddScoped<RequestAuthorizationService>();
builder.Services.AddScoped<TrackerOperations>();
builder.Services.AddScoped<OrganizationOperations>();
builder.Services.AddScoped<IdentityBootstrap>();
builder.Services.AddHttpClient("auth0-userinfo")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddTransient<OccupancyDbInitializer>();
builder.Services.AddSingleton<ISendGridFactory, SendGridFactory>();
builder.Services.AddHostedService<DatabaseInitializationService>();
builder.Services.AddHostedService<OccupancyLogProcessor>();
builder.Services.AddHostedService<OccupancyEmailProcessor>();

var app = builder.Build();
_ = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<JwtBearerOptions>>()
    .Get(JwtBearerDefaults.AuthenticationScheme);
if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("Occupancy")))
    throw new InvalidOperationException("Connection string 'Occupancy' not found.");
app.UseHttpsRedirection();
app.UseEnyimMemcached();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/public-content/{page}", async (string page, IDbContextFactory<OccupancyContext> contexts) =>
{
    if (page is not ("privacy" or "terms")) return Results.NotFound();
    await using var context = await contexts.CreateDbContextAsync();
    var content = await context.MarkdownText.AsNoTracking()
        .Where(x => x.PageName == page && x.TextIdentifier == "body" && x.CurrentStatus == Statuses.DataStatus.Active.Id)
        .Select(x => x.MarkdownContent).FirstOrDefaultAsync();
    content ??= await context.MarkdownText.AsNoTracking()
        .Where(x => x.PageName == "" && x.TextIdentifier == "body" && x.CurrentStatus == Statuses.DataStatus.Active.Id)
        .Select(x => x.MarkdownContent).FirstOrDefaultAsync();
    return content == null ? Results.NotFound() : Results.Ok(content);
});

app.MapPost("/api/me/bootstrap", async (HttpContext context, IdentityBootstrap identity, CancellationToken cancellationToken) =>
{
    try { return Results.Ok(await identity.ResolveAsync(context, cancellationToken)); }
    catch (SecurityException) { return Results.Forbid(); }
}).RequireAuthorization();

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

app.MapGet("/api/organizations", async (RequestAuthorizationService requestAuthorization,
    IOccAuthorizationService authorization, IDbContextFactory<OccupancyContext> contexts) =>
{
    try
    {
        var actor = await requestAuthorization.GetCurrentUserAsync();
        if (actor == null) return Results.Forbid();
        await using var context = await contexts.CreateDbContextAsync();
        if (actor.IsSuperAdmin)
            return Results.Ok(await context.Organizations.AsNoTracking().ToListAsync());
        var roles = await authorization.GetUserRolesFilteredAsync(actor.UserInformationSqid!);
        var visible = roles.Select(x => x.OrganizationSqid).Distinct()
            .Where(id => !string.IsNullOrEmpty(ResourceAuthorization.OrganizationRole(roles, id, false))).ToList();
        var administrators = visible.Where(id => ResourceAuthorization.OrganizationRole(roles, id, false) ==
            AuthorizationRecords.Roles.OrganizationAdmin.Name).ToList();
        var organizations = await context.Organizations.AsNoTracking()
            .Where(x => x.OrganizationSqid != null &&
                ((x.CurrentStatus == 0 && visible.Contains(x.OrganizationSqid)) ||
                 (x.CurrentStatus == 1 && administrators.Contains(x.OrganizationSqid))))
            .ToListAsync();
        return Results.Ok(organizations);
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

app.MapGet("/api/trackers/{counterSqid}", async (string counterSqid, TrackerOperations tracker) =>
    await ExecuteAsync(() => tracker.ReadAsync(counterSqid)));
app.MapPost("/api/trackers/{counterSqid}/count", async (string counterSqid, CounterChange change, TrackerOperations tracker) =>
    await ExecuteAsync(() => tracker.UpdateAsync(counterSqid, change.Delta)));

app.Run();

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
