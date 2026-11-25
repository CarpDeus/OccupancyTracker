using Auth0.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

using OccupancyTracker.Components;
using OccupancyTracker.IService;
using OccupancyTracker.Models;
using OccupancyTracker.Service;
using Microsoft.AspNetCore.ResponseCompression;
using System.Text.Json.Serialization;
using MudBlazor;
using System.Text.Json;

namespace OccupancyTracker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            builder.Services.AddMudServices();
            builder.Services.AddMudMarkdownServices();

            builder.Services.AddScoped<TokenProvider>();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<IRequestAuthorizationService, RequestAuthorizationService>();

            builder.Services.AddCascadingAuthenticationState();
            builder.Services.AddCascadingValue("userInformation", sp => new UserInformation());

            var auth0Audience = builder.Configuration["Auth0:Audience"];
            if (string.IsNullOrWhiteSpace(auth0Audience))
                throw new InvalidOperationException("Auth0:Audience must be configured.");
            builder.Services.AddAuth0WebAppAuthentication(options =>
            {
                options.Domain = builder.Configuration["Auth0:Domain"];
                options.ClientId = builder.Configuration["Auth0:ClientId"];
                options.ClientSecret = builder.Configuration["Auth0:ClientSecret"]
                    ?? throw new InvalidOperationException("Auth0:ClientSecret must be configured.");
                options.Scope = "openid profile email address phone";
            }).WithAccessToken(options =>
            {
                options.Audience = auth0Audience;
            });
            builder.Services.PostConfigure<OpenIdConnectOptions>(Auth0Constants.AuthenticationScheme, options =>
            {
                options.GetClaimsFromUserInfoEndpoint = true;
                options.SaveTokens = true;
                options.ClaimActions.MapUniqueJsonKey("email_verified", "email_verified");
            });

            var apiAddress = builder.Configuration["Api:BaseAddress"]
                ?? throw new InvalidOperationException("Api:BaseAddress must be configured.");
            if (!Uri.TryCreate(apiAddress, UriKind.Absolute, out var apiUri) ||
                !string.IsNullOrEmpty(apiUri.UserInfo) || !string.IsNullOrEmpty(apiUri.Query) ||
                !string.IsNullOrEmpty(apiUri.Fragment) ||
                (apiUri.Scheme != Uri.UriSchemeHttps &&
                 !(builder.Environment.IsDevelopment() && apiUri.IsLoopback && apiUri.Scheme == Uri.UriSchemeHttp)))
                throw new InvalidOperationException("Api:BaseAddress must use HTTPS except for localhost in development.");
            builder.Services.AddHttpClient<TrackerApiClient>(client =>
                client.BaseAddress = new Uri(apiUri.AbsoluteUri.TrimEnd('/') + "/"))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            builder.Services.AddHttpClient<ProfileApiClient>(client =>
                client.BaseAddress = new Uri(apiUri.AbsoluteUri.TrimEnd('/') + "/"))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            builder.Services.AddHttpClient<OrganizationApiClient>(client =>
                client.BaseAddress = new Uri(apiUri.AbsoluteUri.TrimEnd('/') + "/"))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            builder.Services.AddHttpClient<PublicContentApiClient>(client =>
                client.BaseAddress = new Uri(apiUri.AbsoluteUri.TrimEnd('/') + "/"))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

            builder.Services.AddEnyimMemcached(builder.Configuration.GetSection("enyimMemcached"));
            //builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            //builder.Services.AddDatabaseDeveloperPageExceptionFilter();


            var occupancyConnectionString = builder.Configuration.GetConnectionString("Occupancy") ?? throw new InvalidOperationException("Connection string 'Occupancy' not found.");
            builder.Services.AddDbContextFactory<OccupancyContext>(options =>
                 options.UseSqlServer(occupancyConnectionString), 
                 ServiceLifetime.Transient
                );

            //builder.Services.AddScoped<IProfileService, ProfileService>();
            builder.Services.AddSingleton<NavigatorService>();
                List<SqidAlphabet> sqidAlphabets = builder.Configuration.GetSection("Sqids").Get<List<SqidAlphabet>>();
                if (sqidAlphabets.Count == 0)
                {
                    throw new InvalidOperationException("Sqids Alphabets not provided!");
                }
            // Remove or comment out the direct SqidsEncoder registration
            // builder.Services.AddSingleton(new SqidsEncoder<long>(new() { ... }));

            // Register the factory
            builder.Services.AddSingleton<ISqidsEncoderFactory, SqidsEncoderFactory>();


            //builder.Services.AddDbContext<OccupancyContext>(options =>
            //    options.UseSqlServer(occupancyConnectionString));
            builder.Services.AddSingleton<IOccAuthorizationService, OccAuthorizationService>();
            builder.Services.AddScoped<IOrganizationService, OrganizationService>();
            builder.Services.AddScoped<ILocationService, LocationService>();
            builder.Services.AddScoped<IEntranceService, EntranceService>();
            builder.Services.AddScoped<IEntranceCounterService, EntranceCounterService>();
            builder.Services.AddScoped<IOrganizationUserService, OrganizationUserService>();
            builder.Services.AddScoped<IAdminUtilsService, AdminUtilsService>();
            builder.Services.AddSingleton<ISendGridFactory, SendGridFactory>();

            builder.Services.AddMvc()
                .AddJsonOptions(o =>
                {
                    o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                }
                );
            builder.Services.AddControllersWithViews()
               .AddJsonOptions(o =>
               {
                   o.JsonSerializerOptions.PropertyNamingPolicy = null;
                   o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                   o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
               });

            // SignalR
            builder.Services.AddSignalR();

            builder.Services.AddResponseCompression(opts =>
            {
                opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                    ["application/octet-stream"]);
            });

            


            var app = builder.Build();

            app.UseResponseCompression();
            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseEnyimMemcached();
            app.UseStaticFiles();
            app.UseAntiforgery();
            app.MapGet("/Account/Login", async (HttpContext httpContext, string returnUrl = "/") =>
            {
                if (!LocalRedirect.IsSafe(returnUrl))
                {
                    httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                var authenticationProperties = new LoginAuthenticationPropertiesBuilder()
                        .WithAudience(auth0Audience)
                        .WithRedirectUri($"/sso-post-signon?returnUrl={Uri.EscapeDataString(returnUrl)}")
                        .Build();
                await httpContext.ChallengeAsync(Auth0Constants.AuthenticationScheme, authenticationProperties);
            });

            app.MapGet("/Account/Logout", async (HttpContext httpContext) =>
            {
                var authenticationProperties = new LogoutAuthenticationPropertiesBuilder()
                        .WithRedirectUri("/")
                        .Build();

                await httpContext.SignOutAsync(Auth0Constants.AuthenticationScheme, authenticationProperties);
                await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            });
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
