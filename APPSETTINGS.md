# `appsettings.json` configuration

**Warning: Do not commit secrets or real credentials to `appsettings.json` or
any other tracked file.** Use a secret manager, deployment secrets, environment
variables, or .NET user secrets for connection strings, client secrets, and API
keys. The values in these examples are placeholders and must be replaced.

The current solution has two executable hosts that use configuration:
`Monolithic/OccupancyTracker.Api` and `Monolithic/OccupancyTracker`. The
`OccupancyTracker.Backend` and `OccupancyTracker.Shared` projects are libraries
and do not have their own `appsettings.json`.

## API: `Monolithic/OccupancyTracker.Api/appsettings.json`

The API requires its SQL Server connection string, OIDC provider and JWT settings,
Sqids alphabets, persistent Data Protection keys, and a Memcached server. Email delivery and invitation links
also require the SendGrid API key and the public UI hostname.

```json
{
  "ConnectionStrings": {
    "Occupancy": "Server=<sql-server>;Database=<database>;User Id=<user>;Password=<database-password>;Encrypt=True;TrustServerCertificate=False;"
  },
  "Oidc": {
    "Authority": "https://<identity-host>/<provider-path>",
    "ClientId": "<api-client-id>",
    "ClientSecret": "<secret-from-secret-store>",
    "CallbackPath": "/signin-oidc",
    "PublicOrigin": "https://<api-hostname>",
    "UiCallback": "https://<ui-hostname>/sso-callback",
    "GroupClaim": "groups",
    "SuperAdminGroup": "<exact-admin-group-name>",
    "Scopes": [ "openid", "profile", "email", "groups" ]
  },
  "Jwt": {
    "Issuer": "<api-token-issuer>",
    "Audience": "<api-token-audience>",
    "SigningKey": "<base64-encoded-random-key>",
    "ExpiryMinutes": 60
  },
  "DataProtection": {
    "KeyPath": "<shared-api-key-directory>"
  },
  "TrustedProxies": [ "<trusted-proxy-ip>" ],
  "DatabaseInitialization": { "Enabled": true },
  "BackgroundProcessors": { "Enabled": true },
  "Sqids": [
    {
      "AlphabetName": "Default",
      "Alphabet": "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
    },
    {
      "AlphabetName": "UserInformation",
      "Alphabet": "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
    }
  ],
  "enyimMemcached": {
    "Servers": [
      {
        "Address": "<memcached-host>",
        "Port": 11211
      }
    ]
  },
  "SendGridKey": "<sendgrid-api-key>",
  "OccupancyDomain": "<public-ui-hostname>"
}
```

Register the API as a confidential OIDC client using authorization-code flow.
Register the exact provider callback `PublicOrigin` + `CallbackPath` (for example
`https://api.example.com/signin-oidc`); the UI callback is not the provider
callback. `UiCallback` is a single trusted, absolute UI URL and must end in
`/sso-callback`. The API never accepts a caller-provided UI return URL.
`Authority` uses the provider's discovery URL base. Keep it unchanged to retain
existing subject links; after changing providers, verified email is required to
link the new subject to an existing account, except for configured SuperAdmin
group members whose email is assumed verified.

Configure the provider to return `email`, `email_verified`, and the configured
`GroupClaim` in the ID token or UserInfo response. The group claim may be an
array or repeated string claims. For Authentik, configure an OIDC provider and
scope/property mappings to emit `groups` and verified email; group values must
match `SuperAdminGroup` exactly, including case. Adjust `Scopes` and `GroupClaim`
for other providers; `openid` is required. Missing groups grant no SuperAdmin.
Only the configured group grants SuperAdmin; existing stored grants and the
first-user bootstrap do not grant it. Membership is captured on sign-in and
remains effective until the API token expires. Organization and location roles
are managed locally.

Membership in the configured `SuperAdminGroup` also establishes email
verification for account linking when `email_verified` is false or absent.
A non-empty email is still required. Other users must supply
`email_verified: true`. This trusts the provider's SuperAdmin group assignment
to authorize linking an existing account by email; restrict who can assign
membership to that group. Stored privileges and unmapped provider role claims
do not bypass verification.

A 403 at `/api/auth/callback` means OIDC authentication completed but local
account resolution was rejected. Check the API's `OidcSignIn` warning using
the response's `traceId`. A new subject requires a non-empty email and
`email_verified: true` unless it belongs to the configured SuperAdmin group;
inactive local accounts are also rejected. Missing groups alone do not prevent
sign-in, but grant no SuperAdmin access.
For Authentik, check the selected email scope/property mapping and ensure it
reflects actual email verification. Since Authentik 2025.10 the default email
mapping returns `email_verified: false`. Create a custom Scope Mapping with
scope name `email`, then replace the provider's default email mapping with it
(do not select both). If your verification flow records the confirmed address
in `email_verified_address`, use:

```python
email = request.user.email
return {
    "email": email,
    "email_verified": bool(email and request.user.attributes.get("email_verified_address") == email),
}
```

The attribute must be populated by a real verification process, not by this
mapping. Follow Authentik's **Verify email addresses** guide to configure that
process. Do not hard-code `email_verified` to true
for unverified users or disable this check: verified email is used to link
existing accounts.

Generate `Jwt:SigningKey` from at least 32 cryptographically random bytes and
base64-encode it. Keep it private and identical across API replicas. Tokens
are signed with HS256, checked for issuer, audience and expiry, and have a
configurable lifetime of 1–1440 minutes (default 60). Signing-key rotation
invalidates existing sessions.

Each API replica must share the same persistent `DataProtection:KeyPath` and
database: OIDC correlation cookies and encrypted, single-use, five-minute
exchange codes must work across replicas. Restrict key-directory access and
encrypt keys at rest using your deployment's key-management facilities; loss
or exposure of these keys affects sessions. Configure only trusted proxy IPs in
`TrustedProxies` for forwarded scheme information; never trust arbitrary
forwarded headers. The API issues redirects to the configured public origin.
Avoid logging query strings on the provider callback and UI `/sso-callback`:
they contain short-lived authorization or exchange codes.
Set `BackgroundProcessors:Enabled` and `DatabaseInitialization:Enabled` to
`false` on all but one API replica. Start the initializing replica first and
wait for database migration and initialization to finish before starting others.

The `Default`
alphabet is used for organization, location, entrance, counter, and invitation
IDs. `UserInformation` is used for user IDs. Keep each alphabet unchanged once
IDs have been created in a deployed database.

`SendGridKey` and `OccupancyDomain` are needed for email and organization
invitations. Set `OccupancyDomain` to the public UI hostname, without a scheme
or path; invitation links use HTTPS.

## UI: `Monolithic/OccupancyTracker/appsettings.json`

The UI requires the API's browser-reachable absolute base address and persistent
Data Protection keys. It does not need any OIDC client or JWT signing settings.

```json
{
  "Api": {
    "BaseAddress": "https://<api-hostname>/"
  },
  "DataProtection": { "KeyPath": "<shared-ui-key-directory>" },
  "TrustedProxies": [ "<trusted-proxy-ip>" ]
}
```

Use HTTPS for `Api:BaseAddress` outside development. The UI permits HTTP only
for a loopback address while running in the Development environment.
All UI replicas must share their UI key directory. Use a separate directory
from the API, protect it at rest and restrict access. Trust only the proxy IPs
that actually forward requests. Sign-out clears the local UI session; it does
not sign out of the identity provider.

## Local development and deployment

The repository's Development settings already point the UI at the local API.
Keep credentials out of those tracked files too. Supply secrets through the
appropriate secret store or environment variables. For ASP.NET Core environment
variables, use double underscores for nested keys, for example:
`ConnectionStrings__Occupancy`, `Oidc__ClientSecret`, `Jwt__SigningKey`, and `SendGridKey`.

The API initializes and migrates its database at startup, so it must be able to
connect to the configured SQL Server. Run the email and occupancy-log queues on
exactly one API instance.
