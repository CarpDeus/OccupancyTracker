using System.Net.Http.Headers;
using System.Security;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Authorization;
using OccupancyTracker.IService;
using OccupancyTracker.Models;

public sealed class IdentityBootstrap(IHttpClientFactory clients, IConfiguration configuration, IOccAuthorizationService authorization)
{
    public async Task<UserInformation> ResolveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var subjects = context.User.Identities.Where(i => i.IsAuthenticated)
            .SelectMany(i => i.Claims).Where(c => c.Type == "sub" || c.Type == ClaimTypes.NameIdentifier)
            .Select(c => c.Value).Distinct(StringComparer.Ordinal).ToArray();
        if (subjects.Length != 1 || string.IsNullOrWhiteSpace(subjects[0]))
            throw new SecurityException("The authenticated principal must contain exactly one subject.");
        if (!AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out var bearer) ||
            !string.Equals(bearer.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(bearer.Parameter))
            throw new SecurityException("A bearer token is required.");

        var domain = configuration["Auth0:Domain"];
        if (!Uri.TryCreate($"https://{domain}/", UriKind.Absolute, out var authority) ||
            authority.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(authority.UserInfo) ||
            authority.AbsolutePath != "/" || !string.IsNullOrEmpty(authority.Query) ||
            !string.IsNullOrEmpty(authority.Fragment))
            throw new InvalidOperationException("Auth0:Domain must be a hostname.");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(authority, "userinfo"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer.Parameter);
        using var response = await clients.CreateClient("auth0-userinfo").SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var profile = await response.Content.ReadFromJsonAsync<Auth0Profile>(cancellationToken)
            ?? throw new InvalidDataException("Auth0 returned an empty user profile.");
        if (!string.Equals(profile.Subject, subjects[0], StringComparison.Ordinal))
            throw new SecurityException("The Auth0 user profile does not match the token subject.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subjects[0]),
            new(ClaimTypes.Email, profile.Email ?? ""),
            new(ClaimTypes.GivenName, profile.GivenName ?? ""),
            new(ClaimTypes.Surname, profile.FamilyName ?? ""),
            new("picture", profile.Picture ?? ""),
            new("email_verified", profile.EmailVerified ? "true" : "false")
        };
        claims.AddRange(context.User.Identities.Where(i => i.IsAuthenticated)
            .SelectMany(i => i.Claims)
            .Where(c => c.Type == "https://occupancy-tracker.secure-scalable.solutions/roles"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "validated-api-token"));
        return await authorization.GetFromStateAsync(new AuthenticationState(principal))
            ?? throw new SecurityException("The authenticated account is unavailable.");
    }

    private sealed class Auth0Profile
    {
        [JsonPropertyName("sub")] public string? Subject { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonPropertyName("email_verified")] public bool EmailVerified { get; set; }
        [JsonPropertyName("given_name")] public string? GivenName { get; set; }
        [JsonPropertyName("family_name")] public string? FamilyName { get; set; }
        [JsonPropertyName("picture")] public string? Picture { get; set; }
    }
}
