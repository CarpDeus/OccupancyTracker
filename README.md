# OccupancyTracker
The year was 2020 and the pandemic changed many things, including closing all stores. Once stores were opened up, they had strict occupancy rules and there were employees at each door counting the number of people who entered and exited. It occurred to me then that an application to do that would be a fairly simple thing to put together. Fortunately for me, I was kept fully employed during that time so I didn't have time to contemplate it further.

When my job was eliminated in 2024, I realized that this application would be a perferct opportunity for me to learn new skills by implementing it. As I looked at it more closely, however, I realized it could do something else as well, it could work as the perfect example of taking a monolithic codebase and improving it. That's important because the number one rule of any startup should be:

> Getting a product to market, even if it is not fully finished, is a better return then burning money on a product that no one may want.

So I decided to push out a monolithic Blazor server application, using [MudBlazor](https://mudblazor.com/) for the UI, [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/) for data store and [Auth0]([https://www.okta.com/](https://auth0.com/)) for authentication. This application can be found in the [Monolithic](https://github.com/CarpDeus/OccupancyTracker/tree/main/Monolithic) directory in this repo.

The solution is being split into `OccupancyTracker.Shared` (models and DTOs),
`OccupancyTracker.Backend` (services, EF Core context and migrations),
`OccupancyTracker.Api` (tracker, profile, organization, user, location, entrance
and public-content endpoints), and `OccupancyTracker` (the MudBlazor UI). The
split is **not complete**: other UI features still call backend services directly.

### Running the tracker API and UI

Both hosts require the same `ConnectionStrings:Occupancy`, `Sqids`, and
`enyimMemcached` configuration as the existing application. Configure
`Auth0:Domain` (the domain without `https://`) and `Auth0:Audience` in both hosts;
the audience is the identifier of an Auth0 API. The UI also needs its existing
Auth0 client settings, including `Auth0:ClientId` and `Auth0:ClientSecret`.
Configure the Auth0 application callbacks for the UI and issue the custom
super-admin role claim in API access tokens. Supply secrets through your
deployment's secret store, not checked-in configuration.

The API initializes the database and runs the email and occupancy-log
background processors. Migrations live in `OccupancyTracker.Backend`; the
existing EF migration history is preserved. Run only one API instance
responsible for those processors. Configure `SendGridKey` and the existing
email settings on the API. Set `OccupancyDomain` on the API to the public UI
hostname (optionally with a port, without a scheme or path); invitation emails
link to that host over HTTPS. During the migration, the UI still needs database
and cache configuration for its remaining direct service calls.

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

### Authorization

`IRequestAuthorizationService` resolves the acting account from the authenticated
HTTP principal's Auth0 subject, not a user ID supplied by the caller. Organization
and location permissions come from active memberships and are scoped to the
requested resource. A counter is usable anonymously only if its current,
active counter does not require authentication; otherwise the actor must have
access to its location.

The tracker API validates access-token signatures, issuer, audience and expiry.
The super-admin role must be included in the validated access token as
`https://occupancy-tracker.secure-scalable.solutions/roles`.
Never accept a principal, actor ID, or `IsSuperAdmin` value from an API request
body. New SSO account links require an Auth0-verified email address. On sign-in the
API retrieves the profile directly from Auth0's user-info endpoint and checks
that its subject matches the validated access token. The UI requests `openid`,
`profile` and `email` scopes; Auth0 must permit the API audience's access
tokens to call `/userinfo`.

`GET /api/trackers/{counterSqid}` reads a counter and
`POST /api/trackers/{counterSqid}/count` accepts `{"delta": 1}`. Anonymous
counter URLs are capabilities: anyone with an active public counter URL can
read and update it, so handle those URLs as secrets. Protected counters require
a valid token and location access.

`POST /api/me/bootstrap` links or creates the signed-in account using Auth0's
verified profile. `GET /api/me` and `PUT /api/me` read and update only that
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
Membership and role editing remains unimplemented; the previous user-rights UI
was excluded from the build.
`GET /api/public-content/{page}` serves the public `privacy` and `terms` pages.

The remaining backend services accept caller-supplied identities and must not
be exposed directly. Future API endpoints must use request authorization for
every operation and enforce resource and field-level checks.
