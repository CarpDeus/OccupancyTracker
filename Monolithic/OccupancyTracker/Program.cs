using Auth0.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

using OccupancyTracker.Components;
using OccupancyTracker.IService;
using OccupancyTracker.Models;
using OccupancyTracker.Service;
using Microsoft.AspNetCore.ResponseCompression;
using OccupancyTracker.Hubs;
using System.Text.Json.Serialization;
using MudBlazor;

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

            builder.Services.AddCascadingAuthenticationState();
            builder.Services.AddCascadingValue("userInformation", sp => new UserInformation());

            builder.Services.AddAuth0WebAppAuthentication(options =>
            {
                options.Domain = builder.Configuration["Auth0:Domain"];
                options.ClientId = builder.Configuration["Auth0:ClientId"];
                options.Scope = "openid profile email address phone";
            });

            builder.Services.AddHostedService<OccupancyLogProcessor>();
            builder.Services.AddHostedService<OccupancyEmailProcessor>();
            builder.Services.AddEnyimMemcached(builder.Configuration.GetSection("enyimMemcached"));
            //builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            //builder.Services.AddDatabaseDeveloperPageExceptionFilter();


            var occupancyConnectionString = builder.Configuration.GetConnectionString("Occupancy") ?? throw new InvalidOperationException("Connection string 'Occupancy' not found.");
            builder.Services.AddDbContextFactory<OccupancyContext>(options =>
                 options.UseSqlServer(occupancyConnectionString), 
                 ServiceLifetime.Transient
                );
            builder.Services.AddTransient<OccupancyDbInitializer>();

            //builder.Services.AddScoped<IProfileService, ProfileService>();
            builder.Services.AddSingleton<NavigatorService>();

            List<SqidAlphabet> sqidAlphabets = builder.Configuration.GetSection("Occupancy:Sqids").Get<List<SqidAlphabet>>() ?? throw new InvalidOperationException("Sqids Configuration not found!");
            if (sqidAlphabets.Count == 0)
            {
                throw new InvalidOperationException("Sqids Configuration not found!");
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
            builder.Services.AddScoped<IMarkdownTextService, MarkdownTextService>();

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

            using var scope = app.Services.CreateScope();

            var services = scope.ServiceProvider;

            var initialiser = services.GetRequiredService<OccupancyDbInitializer>();

            initialiser.Run();

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
                var authenticationProperties = new LoginAuthenticationPropertiesBuilder()
                        //.WithRedirectUri(returnUrl)
                        .WithRedirectUri($"/sso-post-signon/?returnUrl={returnUrl}")
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

            app.MapHub<ChatHub>("/UpdateOccupancy");
            
            
            app.Run();
        }
    }
}
