using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using MudBlazor.Services;

using OccupancyTracker.Components;
using OccupancyTracker.Models;
using OccupancyTracker.Service;
using Microsoft.AspNetCore.ResponseCompression;
using System.Text.Json.Serialization;
using MudBlazor;
using System.Text.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using System.Net.Http.Json;

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
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                foreach (var address in builder.Configuration.GetSection("TrustedProxies").Get<string[]>() ?? [])
                    options.KnownProxies.Add(System.Net.IPAddress.Parse(address));
            });

            builder.Services.AddCascadingAuthenticationState();
            builder.Services.AddCascadingValue("userInformation", sp => new UserInformation());

            var keyPath = builder.Configuration["DataProtection:KeyPath"];
            if (string.IsNullOrWhiteSpace(keyPath))
                throw new InvalidOperationException("DataProtection:KeyPath must be configured to a persistent shared directory.");
            builder.Services.AddDataProtection().SetApplicationName("OccupancyTracker.UI")
                .PersistKeysToFileSystem(new DirectoryInfo(keyPath));
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.SlidingExpiration = false;
            });

            var apiAddress = builder.Configuration["Api:BaseAddress"]
                ?? throw new InvalidOperationException("Api:BaseAddress must be configured.");
            if (!Uri.TryCreate(apiAddress, UriKind.Absolute, out var apiUri) ||
                !string.IsNullOrEmpty(apiUri.UserInfo) || !string.IsNullOrEmpty(apiUri.Query) ||
                !string.IsNullOrEmpty(apiUri.Fragment) ||
                (apiUri.Scheme != Uri.UriSchemeHttps &&
                 !(builder.Environment.IsDevelopment() && apiUri.IsLoopback && apiUri.Scheme == Uri.UriSchemeHttp)))
                throw new InvalidOperationException("Api:BaseAddress must use HTTPS except for localhost in development.");
            apiUri = new Uri(apiUri.AbsoluteUri.TrimEnd('/') + "/");
            builder.Services.AddHttpClient("sso", client =>
                client.BaseAddress = new Uri(apiUri.AbsoluteUri.TrimEnd('/') + "/"))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
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
            builder.Services.AddHttpClient<FacilityApiClient>(client =>
                client.BaseAddress = new Uri(apiUri.AbsoluteUri.TrimEnd('/') + "/"))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            builder.Services.AddHttpClient<OrganizationUserApiClient>(client =>
                client.BaseAddress = new Uri(apiUri.AbsoluteUri.TrimEnd('/') + "/"))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            builder.Services.AddHttpClient<AdminApiClient>(client =>
                client.BaseAddress = new Uri(apiUri.AbsoluteUri.TrimEnd('/') + "/"))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            builder.Services.AddSingleton<NavigatorService>();

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

            app.UseForwardedHeaders();
            app.UseResponseCompression();
            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseAntiforgery();
            var loginCookieName = app.Environment.IsDevelopment() ? "OccupancyTracker.Login" : "__Host-OccupancyTracker.Login";
            app.MapGet("/Account/Login", (HttpContext httpContext, IDataProtectionProvider protection, string returnUrl = "/") =>
            {
                if (!LocalRedirect.IsSafe(returnUrl))
                    return Results.BadRequest();
                var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
                var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
                var payload = protection.CreateProtector("OccupancyTracker.Login.v1").Protect(verifier + "\n" + returnUrl);
                httpContext.Response.Cookies.Append(loginCookieName, payload, new CookieOptions
                {
                    HttpOnly = true, Secure = !app.Environment.IsDevelopment() || httpContext.Request.IsHttps, SameSite = SameSiteMode.Lax,
                    MaxAge = TimeSpan.FromMinutes(5), IsEssential = true, Path = "/"
                });
                httpContext.Response.Headers.CacheControl = "no-store";
                return Results.Redirect(QueryHelpers.AddQueryString(new Uri(apiUri, "api/auth/sign-in").ToString(), "challenge", challenge));
            });

            app.MapGet("/sso-callback", async (HttpContext context, IDataProtectionProvider protection,
                IHttpClientFactory clients, string? code) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                if (string.IsNullOrWhiteSpace(code) || !context.Request.Cookies.TryGetValue(loginCookieName, out var cookie))
                    return Results.BadRequest("Sign-in session is missing.");
                context.Response.Cookies.Delete(loginCookieName, new CookieOptions { Path = "/", Secure = !app.Environment.IsDevelopment() || context.Request.IsHttps });
                string payload;
                try { payload = protection.CreateProtector("OccupancyTracker.Login.v1").Unprotect(cookie); }
                catch (CryptographicException) { return Results.BadRequest("Sign-in session is invalid."); }
                var parts = payload.Split('\n', 2);
                if (parts.Length != 2 || !LocalRedirect.IsSafe(parts[1]))
                    return Results.BadRequest("Sign-in session is invalid.");
                using var response = await clients.CreateClient("sso").PostAsJsonAsync("api/auth/exchange", new { code, verifier = parts[0] });
                if (!response.IsSuccessStatusCode)
                    return Results.Problem("Sign-in could not be completed.", statusCode: StatusCodes.Status502BadGateway);
                var result = await response.Content.ReadFromJsonAsync<SsoExchangeResponse>();
                if (result == null || string.IsNullOrEmpty(result.AccessToken) || result.ExpiresUtc <= DateTime.UtcNow)
                    return Results.Problem("The API returned an invalid sign-in response.", statusCode: StatusCodes.Status502BadGateway);
                var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "Signed-in user")], CookieAuthenticationDefaults.AuthenticationScheme);
                var properties = new AuthenticationProperties { ExpiresUtc = new DateTimeOffset(result.ExpiresUtc.ToUniversalTime()) };
                properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = result.AccessToken }]);
                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);
                var destination = result.HasCompletedRegistration ? parts[1] :
                    $"/profile/edit?returnUrl={Uri.EscapeDataString(parts[1])}";
                return Results.LocalRedirect(destination);
            });

            app.MapGet("/Account/Logout", async (HttpContext httpContext) =>
            {
                await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                httpContext.Response.Redirect("/");
            });
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }

        internal sealed record SsoExchangeResponse(string AccessToken, DateTime ExpiresUtc, bool HasCompletedRegistration);
    }
}
