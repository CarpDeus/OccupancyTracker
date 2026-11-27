using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

public sealed class SsoOptions
{
    public required string Authority { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required string CallbackPath { get; init; }
    public required Uri PublicOrigin { get; init; }
    public required Uri UiCallback { get; init; }
    public required string GroupClaim { get; init; }
    public required string SuperAdminGroup { get; init; }
    public required string[] Scopes { get; init; }
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required byte[] SigningKey { get; init; }
    public required int ExpiryMinutes { get; init; }

    public static SsoOptions Load(IConfiguration config, bool development)
    {
        string Require(string key) => !string.IsNullOrWhiteSpace(config[key])
            ? config[key]! : throw new InvalidOperationException($"{key} must be configured.");
        var authority = Require("Oidc:Authority");
        var callback = Require("Oidc:CallbackPath");
        var uiCallback = Require("Oidc:UiCallback");
        var publicOrigin = Require("Oidc:PublicOrigin");
        static bool SafeUri(string value, bool development, out Uri? uri) =>
            Uri.TryCreate(value, UriKind.Absolute, out uri) && string.IsNullOrEmpty(uri.UserInfo) &&
            string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
            (uri.Scheme == Uri.UriSchemeHttps || (development && uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp));
        if (!SafeUri(authority, development, out _) || !SafeUri(uiCallback, development, out var uiUri) ||
            uiUri!.AbsolutePath != "/sso-callback" || !SafeUri(publicOrigin, development, out var origin) || origin!.AbsolutePath != "/")
            throw new InvalidOperationException("OIDC URLs must use HTTPS (HTTP loopback is allowed in development); PublicOrigin must be an origin and UiCallback must use /sso-callback.");
        if (!callback.StartsWith('/') || callback.StartsWith("//") || callback.Contains('?') || callback.Contains('#') ||
            callback.Contains('\\') || callback.Any(char.IsControl))
            throw new InvalidOperationException("Oidc:CallbackPath must be an absolute path.");
        byte[] key;
        try { key = Convert.FromBase64String(Require("Jwt:SigningKey")); }
        catch (FormatException ex) { throw new InvalidOperationException("Jwt:SigningKey must be base64-encoded.", ex); }
        if (key.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 random bytes.");
        var expiry = config.GetValue("Jwt:ExpiryMinutes", 60);
        if (expiry is < 1 or > 1440)
            throw new InvalidOperationException("Jwt:ExpiryMinutes must be between 1 and 1440.");
        var scopes = config.GetSection("Oidc:Scopes").Get<string[]>() ?? ["openid", "profile", "email", "groups"];
        if (!scopes.Contains("openid", StringComparer.Ordinal))
            throw new InvalidOperationException("Oidc:Scopes must include openid.");
        if (string.IsNullOrWhiteSpace(config["Oidc:GroupClaim"] ?? "groups"))
            throw new InvalidOperationException("Oidc:GroupClaim must not be empty.");
        return new SsoOptions
        {
            Authority = authority.TrimEnd('/'), ClientId = Require("Oidc:ClientId"),
            ClientSecret = Require("Oidc:ClientSecret"), CallbackPath = callback, PublicOrigin = origin!, UiCallback = uiUri!,
            GroupClaim = config["Oidc:GroupClaim"] ?? "groups", SuperAdminGroup = Require("Oidc:SuperAdminGroup"),
            Scopes = scopes, Issuer = Require("Jwt:Issuer"), Audience = Require("Jwt:Audience"),
            SigningKey = key, ExpiryMinutes = expiry
        };
    }

    public static string Hash(string value) =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
