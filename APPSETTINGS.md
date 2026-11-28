# `appsettings.json` configuration

**Warning: Do not commit secrets or real credentials to `appsettings.json` or
any other tracked file.** Use a secret manager, deployment secrets, environment
variables, or .NET user secrets for connection strings, client secrets, and API
keys. The values in these examples are placeholders and must be replaced.

The current solution has two executable hosts that use configuration:
`Monolithic/OccupancyTracker.Api` and `Monolithic/OccupancyTracker`. The
`OccupancyTracker.Backend` and `OccupancyTracker.Shared` projects are libraries
and do not have their own `appsettings.json`.

Both hosts use the standard ASP.NET Core configuration providers. Environment
variables override matching values in `appsettings.{Environment}.json` and
`appsettings.json`; file values remain the fallback when no environment value
is supplied. Command-line arguments have the highest priority. This applies to
all settings, including connection strings, OIDC/JWT, API addresses, cache,
arrays, and feature flags. Use `__` for nested keys and numeric array indices
(for example `enyimMemcached__Servers__0__Address`). Empty or invalid overrides
are not silently replaced with file values.

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
    "RequireVerifiedEmail": true,
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
link the new subject to an existing account when `RequireVerifiedEmail` is
enabled, except for configured SuperAdmin group members whose email is assumed verified.

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
`email_verified: true` when `Oidc:RequireVerifiedEmail` is enabled (default `true`).
Set it to `false`, or set the API environment variable
`Oidc__RequireVerifiedEmail=false`, to allow account linking when the provider
reports false or omits verification. Restart the API after changing this option.
Disabling verification trusts provider-supplied emails to link existing accounts,
so use it only with a trusted provider that controls email assignment.
This trusts the provider's SuperAdmin group assignment
to authorize linking an existing account by email; restrict who can assign
membership to that group. Stored privileges and unmapped provider role claims
do not bypass verification.

A 403 at `/api/auth/callback` means OIDC authentication completed but local
account resolution was rejected. Check the API's `OidcSignIn` warning using
the response's `traceId`. A new subject requires a non-empty email and
`email_verified: true` when verification is required, unless it belongs to the configured SuperAdmin group;
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
process. Do not hard-code `email_verified` to true for unverified users.
Use `Oidc:RequireVerifiedEmail` to control the application check explicitly.

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
    "BaseAddress": "https://<api-hostname>/",
    "InternalBaseAddress": "http://api:8080/"
  },
  "DataProtection": { "KeyPath": "<shared-ui-key-directory>" },
  "TrustedProxies": [ "<trusted-proxy-ip>" ]
}
```

Use HTTPS for `Api:BaseAddress` outside development. The UI permits HTTP only
for a loopback address while running in the Development environment.
`Api:InternalBaseAddress` is optional and defaults to `Api:BaseAddress`.
It controls all server-to-server requests, including readiness and token
exchange, but never browser redirects. An explicitly configured internal address
may use HTTP on a trusted private network; tokens travel unencrypted on that
connection, so use HTTPS when the network is not isolated.
All UI replicas must share their UI key directory. Use a separate directory
from the API, protect it at rest and restrict access. Trust only the proxy IPs
that actually forward requests. Sign-out clears the local UI session; it does
not sign out of the identity provider.

## Local development and deployment

Both hosts enable HTTP-to-HTTPS redirects by default. Set
`HttpsRedirection:Enabled` to `false` in each host's configuration, or
`HttpsRedirection__Enabled=false` in both `.env.api` and `.env.ui`, when a
reverse proxy handles TLS and redirects. Restart the hosts after changing it.
This disables only the application's redirect middleware: production public
URLs must still use HTTPS, secure cookies and UI HSTS remain enabled, and
forwarded headers must still come from configured trusted proxies.

The repository's Development settings already point the UI at the local API.
Keep credentials out of those tracked files too. Supply secrets through the
appropriate secret store or environment variables. For ASP.NET Core environment
variables, use double underscores for nested keys, for example:
`ConnectionStrings__Occupancy`, `Oidc__ClientSecret`, `Jwt__SigningKey`, and `SendGridKey`.

The API initializes and migrates its database at startup, so it must be able to
connect to the configured SQL Server. Run the email and occupancy-log queues on
exactly one API instance.

## Docker

The root `Dockerfile` has `api` and `ui` targets using .NET 10 multi-stage builds.
Both run as the non-root `app` user, listen on port 8080, and include health checks.
The UI image contains the complete publish output, including MudBlazor assets.
Build separately with `docker build --target api -t occupancy-api .` and
`docker build --target ui -t occupancy-ui .`, or use Compose:

```sh
cp .env.api.example .env.api
cp .env.ui.example .env.ui
# Edit .env.api and .env.ui before starting.
docker compose up --build -d
docker compose ps
docker compose logs api ui
```

The example files are for **local Development only** and contain placeholders,
not working credentials. They disable application HTTPS redirects so the
containers can serve HTTP locally or behind a TLS proxy.
Generate a signing key with `openssl rand -base64 32`.
Register `http://localhost:5236/signin-oidc` with your OIDC provider if it allows
loopback HTTP redirects. Otherwise use HTTPS public endpoints through a proxy.
The UI is at `http://localhost:5195`. Both published ports bind only to loopback.
The UI can start while the API is initializing; login shows the readiness
waiting page until the API is available.

Compose starts Memcached without exposing its port to the host. SQL Server and
the OIDC provider must already be reachable from the API container; `localhost`
in a connection string refers to the container, not your host. Use the database's
network hostname and credentials, and configure its firewall/TLS appropriately.
For an existing deployment, retain its Sqids alphabets and JWT signing key.
Supply a valid SendGrid key and HTTPS UI hostname for invitations; local HTTP
development cannot exercise HTTPS invitation links without a proxy.

`.env.api` and `.env.ui` are ignored by Git and excluded from Docker builds.
Compose injects their settings at runtime; never pass secrets as build arguments
or put them in image layers. Environment-file secrets are visible to container
administrators. Use your deployment's secret-management facilities for production.
Keep tracked appsettings files free of credentials. To supply an untracked JSON
fallback, mount it read-only at `/app/appsettings.Production.json` rather than
copying it into the image; environment variables still override its keys.

The API and UI have separate named key volumes mounted at
`/var/lib/occupancy/keys`, the image's default `DataProtection:KeyPath`. Compose
initializes them with the image directory's `app` ownership. Custom bind mounts
must be writable by UID 1654. Protect and back up both key volumes.
`docker compose down` preserves them; `docker compose down -v` deletes them and
invalidates sessions and outstanding exchange codes.

For production, set `ASPNETCORE_ENVIRONMENT=Production` in both environment
files, set `Api__BaseAddress`, `Oidc__PublicOrigin`, `Oidc__UiCallback`, and
`OccupancyDomain` to your public HTTPS endpoints, and place both hosts behind
your TLS reverse proxy. Keep `Api__InternalBaseAddress=http://api:8080/` only on
a trusted private network (or configure internal HTTPS). Set
`HttpsRedirection__Enabled=false` on both hosts when the proxy handles HTTPS
redirects; this also lets the API serve internal HTTP requests without redirecting.
Configure
`TrustedProxies__0` with the actual proxy IP on each host so forwarded HTTPS
schemes are honored; Blazor also needs WebSocket forwarding. Do not enable
blanket forwarded-header trust. The provided Compose file is a single-instance
development setup; apply the replica/background-processor rules above when
scaling and share each host's key volume across its replicas.
