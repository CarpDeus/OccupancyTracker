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

The API requires its SQL Server connection string, Auth0 issuer and audience,
Sqids alphabets, and a Memcached server. Email delivery and invitation links
also require the SendGrid API key and the public UI hostname.

```json
{
  "ConnectionStrings": {
    "Occupancy": "Server=<sql-server>;Database=<database>;User Id=<user>;Password=<database-password>;Encrypt=True;TrustServerCertificate=False;"
  },
  "Auth0": {
    "Domain": "<your-auth0-tenant>",
    "Audience": "<your-auth0-api-identifier>"
  },
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

`Auth0:Domain` is the tenant hostname without `https://`; `Auth0:Audience` is
the identifier of the Auth0 API and must match the UI's audience. The `Default`
alphabet is used for organization, location, entrance, counter, and invitation
IDs. `UserInformation` is used for user IDs. Keep each alphabet unchanged once
IDs have been created in a deployed database.

`SendGridKey` and `OccupancyDomain` are needed for email and organization
invitations. Set `OccupancyDomain` to the public UI hostname, without a scheme
or path; invitation links use HTTPS.

## UI: `Monolithic/OccupancyTracker/appsettings.json`

The UI requires the API's absolute base address and its Auth0 tenant, API
audience, application client ID, and client secret.

```json
{
  "Api": {
    "BaseAddress": "https://<api-hostname>/"
  },
  "Auth0": {
    "Domain": "<your-auth0-tenant>",
    "Audience": "<your-auth0-api-identifier>",
    "ClientId": "<your-auth0-application-client-id>",
    "ClientSecret": "<your-auth0-application-client-secret>"
  }
}
```

Use HTTPS for `Api:BaseAddress` outside development. The UI permits HTTP only
for a loopback address while running in the Development environment.

## Local development and deployment

The repository's Development settings already point the UI at the local API.
Keep credentials out of those tracked files too. Supply secrets through the
appropriate secret store or environment variables. For ASP.NET Core environment
variables, use double underscores for nested keys, for example:
`ConnectionStrings__Occupancy`, `Auth0__ClientSecret`, and `SendGridKey`.

The API initializes and migrates its database at startup, so it must be able to
connect to the configured SQL Server. Only run one API instance to process the
email and occupancy-log queues.
