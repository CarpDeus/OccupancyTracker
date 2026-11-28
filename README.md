# OccupancyTracker
The year was 2020 and the pandemic changed many things, including closing all stores. Once stores were opened up, they had strict occupancy rules and there were employees at each door counting the number of people who entered and exited. It occurred to me then that an application to do that would be a fairly simple thing to put together. Fortunately for me, I was kept fully employed during that time so I didn't have time to contemplate it further.

When my job was eliminated in 2024, I realized that this application would be a perferct opportunity for me to learn new skills by implementing it. As I looked at it more closely, however, I realized it could do something else as well, it could work as the perfect example of taking a monolithic codebase and improving it. That's important because the number one rule of any startup should be:

> Getting a product to market, even if it is not fully finished, is a better return then burning money on a product that no one may want.

So I decided to push out a monolithic Blazor server application, using [MudBlazor](https://mudblazor.com/) for the UI, [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/) for data storage and OpenID Connect for authentication. This application can be found in the [Monolithic](https://github.com/CarpDeus/OccupancyTracker/tree/main/Monolithic) directory in this repo.

The solution is split into `OccupancyTracker.Shared` (models and DTOs),
`OccupancyTracker.Backend` (services, EF Core context and migrations),
`OccupancyTracker.Api` (tracker, profile, organization, user, location, entrance
and public-content endpoints), and `OccupancyTracker` (the MudBlazor UI).
The UI references the shared models and communicates with the API; it does not
reference the backend or connect directly to the database or cache.

Business and authorization rules live in backend services. API endpoints call
services, which use `IRepositorySessionFactory` rather than EF Core contexts. A session
provides a separate typed `ICrudRepository<T>` for each entity and a shared
unit of work for atomic changes across repositories. Repositories execute CRUD,
filtering, ordering, and relationship loading only; they do not decide access,
status transitions, roles, invitations, or occupancy calculations.
EF Core tracking, bulk deletion, transactions, and migrations are implemented
under `OccupancyTracker.Backend/Repository`; cache writes, deletion, and flushing use a repository
interface. Detached reads are used where projecting request claims must not
change stored identity fields. Existing migration history and schema are unchanged.
The historical `ProfileService.cs` remains excluded from compilation, as before.

### Running the tracker API and UI

Configure `ConnectionStrings:Occupancy`, `Sqids`, and `enyimMemcached` on the
API only. The UI does not need database, Sqids, cache, SendGrid, OIDC provider
credentials, or JWT signing settings. The API owns the OIDC confidential client
and signs application tokens. Both hosts need persistent Data Protection keys.
Supply secrets through your deployment's secret store, not checked-in configuration. See
[APPSETTINGS.md](APPSETTINGS.md) for per-host configuration templates and
placeholder values.

The API initializes the database and runs the email and occupancy-log
background processors. Migrations live in `OccupancyTracker.Backend`; the
existing EF migration history is preserved. In a multi-replica deployment, set
`BackgroundProcessors:Enabled` and `DatabaseInitialization:Enabled` to `false`
on all but one API replica, and start the initializing replica first. Configure `SendGridKey` and the existing
email settings on the API. Set `OccupancyDomain` on the API to the public UI
hostname (optionally with a port, without a scheme or path); invitation emails
link to that host over HTTPS.

In development, the API's `http` launch profile listens on `localhost:5236`;
the UI's development settings point to that address. Start the hosts with:

```sh
dotnet run --project Monolithic/OccupancyTracker.Api --launch-profile http
dotnet run --project Monolithic/OccupancyTracker --launch-profile http
```

In other environments set the UI's `Api:BaseAddress` to the API's HTTPS address.
HTTP is accepted only for loopback in development. Access tokens are sent from
the Blazor server to the API; expired tokens require another sign-in. Occupancy
displays refresh every five seconds rather than using the former public SignalR
hub.

`GET /api/ready` returns `{"ready":true}` after API startup when the database is
reachable, or HTTP 503 with `{"ready":false}` when it is not. Login checks this
endpoint from the UI server and waits, retrying automatically, if the API is
unavailable. Replicas with database initialization disabled still require the
initializing replica to finish first.

The UI maps .NET static-asset endpoints and loads package asset manifests so
MudBlazor CSS and JavaScript are available outside Development too. Deploy the
complete `dotnet publish` output, including `wwwroot` and static-asset manifests.

### Authorization

`IRequestAuthorizationService` resolves the acting account from the authenticated
HTTP principal's API-issued subject, not a user ID supplied by the caller. Organization
and location permissions come from active memberships and are scoped to the
requested resource. A counter is usable anonymously only if its current,
active counter does not require authentication; otherwise the actor must have
access to its location.

The API validates its own access-token signatures, issuer, audience and expiry.
Only membership in the configured OIDC SuperAdmin group grants SuperAdmin.
Membership is evaluated at sign-in and remains in the issued token until expiry;
the first user is not automatically an administrator. Organization and location
roles remain locally managed.
Never accept a principal, actor ID, or `IsSuperAdmin` value from an API request
body. New SSO account links require an OIDC-verified email address by default
(`Oidc:RequireVerifiedEmail`, default `true`), except that
configured SuperAdmin group members' emails are assumed verified. A non-empty
email remains required. Existing
Auth0 accounts can be linked by verified email when signing in with the new
provider. The provider must supply stable subject identifiers, verified emails
and group claims in its ID token or UserInfo response.

`GET /api/trackers/{counterSqid}` reads a counter and
`POST /api/trackers/{counterSqid}/count` accepts `{"delta": 1}`. Anonymous
counter URLs are capabilities: anyone with an active public counter URL can
read and update it, so handle those URLs as secrets. Protected counters require
a valid token and location access.

The API links or creates the account during OIDC sign-in. `GET /api/me` and `PUT /api/me` read and update only that
account's editable profile fields; identity and privilege fields are ignored.
`GET /api/organizations` lists organizations visible to the authenticated
account using active, date-valid memberships and resource-scoped roles.
Organization details, creation, editing and status transitions use authenticated
`/api/organizations` endpoints. Only organization administrators can edit or
change status; recovery of permanently deleted organizations requires a
super-admin. New organizations require a completed profile and, except for
super-admins, an account that does not already belong to an organization.
Location and entrance endpoints are nested under their organization and location.
Location creation requires organization administration; location and entrance
changes require administration of that location. New entrances and their
initial public counters are created together. Existing capability-link
semantics for counters are unchanged.
Organization administrators can list members and outstanding invitations and
queue invitations through `/api/organizations/{orgSqid}/users` and
`/api/organizations/{orgSqid}/invitations`. Invitations can be redeemed via
`POST /api/me/invitations/redeem` only by a completed account with the matching
email address. Redemption does not reactivate denied or expired memberships.
Super-admins can read and edit another user's profile through
`/api/admin/users/{sqid}`; identity and privilege fields cannot be changed there.
Organization administrators and super-admins can edit membership status,
inclusive UTC start/end dates, organization-wide roles, and per-location roles
through `/api/organizations/{orgSqid}/users/{userSqid}`. Super-admin privileges
cannot be assigned there. Concurrent edits are rejected, and an edit cannot
remove administrator coverage on any current or future day previously covered
by the edited administrator. This restriction also applies to super-admins.
Unsupported or duplicate legacy role data must be resolved before editing.
`GET /api/public-content/{page}` serves the public `privacy` and `terms` pages.

`POST /api/admin/cache/flush` is restricted to super-admins and requests a flush
of the API's configured Memcached pool. The cache client only sends the request
to available servers and does not report individual results, so the accepted
response does not confirm that every cache server was flushed.

Legacy backend services accept caller-supplied identities and must not be exposed
directly. Future API endpoints must use request authorization for every operation
and enforce resource and field-level checks.
