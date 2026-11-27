using System.IdentityModel.Tokens.Jwt;
using System.Security;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OccupancyTracker.IService;
using OccupancyTracker.Models;

public sealed class SsoService(
    SsoOptions options, IOccAuthorizationService authorization,
    IDbContextFactory<OccupancyContext> contexts, IDataProtectionProvider protection)
{
    private const string RolesClaim = "https://occupancy-tracker.secure-scalable.solutions/roles";
    private readonly IDataProtector protector = protection.CreateProtector("OccupancyTracker.SsoExchange.v1");

    public async Task<string> CreateExchangeAsync(ClaimsPrincipal external, string challenge)
    {
        var claims = external.Identities.Where(i => i.IsAuthenticated).SelectMany(i => i.Claims).ToList();
        var subjects = claims.Where(c => c.Type is "sub" or ClaimTypes.NameIdentifier)
            .Select(c => c.Value).Distinct(StringComparer.Ordinal).ToArray();
        if (subjects.Length != 1 || string.IsNullOrWhiteSpace(subjects[0]))
            throw new SecurityException("The OIDC identity must contain exactly one subject.");
        var groups = claims.Where(c => c.Type == options.GroupClaim)
            .SelectMany(c => ParseGroups(c.Value)).Distinct(StringComparer.Ordinal).ToArray();
        var isAdmin = groups.Contains(options.SuperAdminGroup, StringComparer.Ordinal);
        var subject = "oidc|" + SsoOptions.Hash(options.Authority + "\n" + subjects[0]);
        var resolvedClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Email, GetClaim(claims, "email", ClaimTypes.Email)),
            new(ClaimTypes.GivenName, GetClaim(claims, "given_name", ClaimTypes.GivenName)),
            new(ClaimTypes.Surname, GetClaim(claims, "family_name", ClaimTypes.Surname)),
            new("picture", GetClaim(claims, "picture")),
            new("email_verified", isAdmin || !options.RequireVerifiedEmail ? "true" : GetClaim(claims, "email_verified"))
        };
        if (isAdmin) resolvedClaims.Add(new Claim(RolesClaim, "SuperAdmin"));
        var user = await authorization.GetFromStateAsync(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity(resolvedClaims, "oidc"))))
            ?? throw new SecurityException("The authenticated account is unavailable.");
        var tokenClaims = new List<Claim> { new("sub", subject) };
        tokenClaims.AddRange(groups.Select(group => new Claim("groups", group)));
        if (isAdmin) tokenClaims.Add(new Claim(RolesClaim, "SuperAdmin"));
        var expiration = DateTime.UtcNow.AddMinutes(options.ExpiryMinutes);
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            options.Issuer, options.Audience, tokenClaims, expires: expiration,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(options.SigningKey), SecurityAlgorithms.HmacSha256)));
        var code = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var payload = JsonSerializer.Serialize(new SsoExchangeResponse(token, expiration, user.HasCompletedRegistration));
        await using var db = await contexts.CreateDbContextAsync();
        await db.SsoExchangeCodes.Where(x => x.ExpiresUtc <= DateTime.UtcNow).ExecuteDeleteAsync();
        db.SsoExchangeCodes.Add(new SsoExchangeCode
        {
            CodeHash = SsoOptions.Hash(code), Challenge = challenge,
            ProtectedToken = protector.Protect(payload), ExpiresUtc = DateTime.UtcNow.AddMinutes(5)
        });
        await db.SaveChangesAsync();
        return code;
    }

    public async Task<SsoExchangeResponse?> ExchangeAsync(string code, string verifier)
    {
        if (!ValidSecret(code) || !ValidSecret(verifier)) return null;
        var codeHash = SsoOptions.Hash(code);
        var challenge = SsoOptions.Hash(verifier);
        await using var db = await contexts.CreateDbContextAsync();
        var entry = await db.SsoExchangeCodes.AsNoTracking().SingleOrDefaultAsync(x => x.CodeHash == codeHash);
        if (entry == null || entry.ExpiresUtc <= DateTime.UtcNow ||
            !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(entry.Challenge), System.Text.Encoding.ASCII.GetBytes(challenge)))
            return null;
        var now = DateTime.UtcNow;
        if (await db.SsoExchangeCodes.Where(x => x.CodeHash == codeHash && x.ExpiresUtc > now).ExecuteDeleteAsync() != 1)
            return null;
        return JsonSerializer.Deserialize<SsoExchangeResponse>(protector.Unprotect(entry.ProtectedToken))
            ?? throw new InvalidDataException("The SSO exchange payload is invalid.");
    }

    public static bool ValidSecret(string value) => value.Length == 43 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static string GetClaim(List<Claim> claims, params string[] types) =>
        claims.FirstOrDefault(c => types.Contains(c.Type, StringComparer.Ordinal))?.Value ?? "";

    private static IEnumerable<string> ParseGroups(string value)
    {
        if (!value.StartsWith('[')) return [value];
        using var document = JsonDocument.Parse(value);
        return document.RootElement.EnumerateArray().Select(x =>
            x.ValueKind == JsonValueKind.String ? x.GetString()! : throw new SecurityException("Invalid group claim.")).ToArray();
    }
}

public sealed record SsoExchangeRequest(string Code, string Verifier);
public sealed record SsoExchangeResponse(string AccessToken, DateTime ExpiresUtc, bool HasCompletedRegistration);
