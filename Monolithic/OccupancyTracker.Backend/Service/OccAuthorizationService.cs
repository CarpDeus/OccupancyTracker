using Enyim.Caching;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

using OccupancyTracker.IService;
using OccupancyTracker.Models;
using Sqids;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Claims;

namespace OccupancyTracker.Service
{
    /// <summary>
    /// Service for handling authorization and user information.
    /// </summary>
    public class OccAuthorizationService : IOccAuthorizationService
    {
        private readonly IDbContextFactory<OccupancyContext> _contextFactory;
        private readonly ISqidsEncoderFactory _sqids;
        private readonly IMemcachedClient _memcachedClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="OccAuthorizationService"/> class.
        /// </summary>
        /// <param name="contextFactory">The context factory.</param>
        /// <param name="sqidsEncoderFactory">The SQIDs encoder factory.</param>
        /// <param name="memcachedClient">The memcached client.</param>
        public OccAuthorizationService(IDbContextFactory<OccupancyContext> contextFactory, ISqidsEncoderFactory sqidsEncoderFactory, IMemcachedClient memcachedClient)
        {
            _memcachedClient = memcachedClient;
            _contextFactory = contextFactory;
            _sqids = sqidsEncoderFactory;
        }

        /// <summary>
        /// Resolves the SSO asynchronously.
        /// </summary>
        /// <param name="auth0Identifier">The Auth0 identifier.</param>
        /// <param name="givenName">The given name.</param>
        /// <param name="surname">The surname.</param>
        /// <param name="picture">The picture URL.</param>
        /// <param name="emailAddress">The email address.</param>
        /// <param name="isSuperAdmin">if set to <c>true</c> [is super admin].</param>
        /// <returns>The user information.</returns>
        private async Task<UserInformation> ResolveSsoAsync(string auth0Identifier, string givenName, string surname, string picture, string emailAddress, bool emailVerified, bool isSuperAdmin)
        {
            static bool IsDeadlock(Exception exception)
            {
                for (Exception? current = exception; current != null; current = current.InnerException)
                {
                    if (current is Microsoft.Data.SqlClient.SqlException { Number: 1205 })
                        return true;
                }
                return false;
            }

            const int maxAttempts = 3;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await ResolveSsoOnceAsync(auth0Identifier, givenName, surname, picture, emailAddress, emailVerified, isSuperAdmin);
                }
                catch (Exception ex) when (attempt < maxAttempts && IsDeadlock(ex))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt));
                }
            }
        }

        private async Task<UserInformation> ResolveSsoOnceAsync(string auth0Identifier, string givenName, string surname, string picture, string emailAddress, bool emailVerified, bool isSuperAdmin)
        {
            using var _context = _contextFactory.CreateDbContext();
            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var sso = await _context.UserSsoInformation.SingleOrDefaultAsync(x => x.Auth0Identifier == auth0Identifier);
            UserInformation? userInformation;

            if (sso != null)
            {
                if (sso.UserInformationId is not > 0 ||
                    !string.Equals(sso.Auth0Identifier, auth0Identifier, StringComparison.Ordinal))
                    throw new System.Security.SecurityException("The authenticated subject is not linked to an account.");
                userInformation = await _context.UserInformation.SingleOrDefaultAsync(x => x.UserInformationId == sso.UserInformationId);
                if (userInformation == null || userInformation.CurrentStatusId != Statuses.DataStatus.Active.Id)
                    throw new System.Security.SecurityException("The authenticated account is unavailable.");
            }
            else
            {
                if (!emailVerified || string.IsNullOrWhiteSpace(emailAddress))
                    throw new System.Security.SecurityException("A verified email address is required to link an account.");
                userInformation = await _context.UserInformation.SingleOrDefaultAsync(x => x.EmailAddress == emailAddress);
                if (userInformation != null && userInformation.CurrentStatusId != Statuses.DataStatus.Active.Id)
                    throw new System.Security.SecurityException("The authenticated account is unavailable.");
                sso = new UserSsoInformation
                {
                    Auth0Identifier = auth0Identifier,
                    GivenName = givenName,
                    Surname = surname,
                    Picture = picture,
                    EmailAddress = emailAddress,
                    UserInformationId = userInformation?.UserInformationId
                };

                _context.UserSsoInformation.Add(sso);
                await _context.SaveChangesAsync();
            }

            if (userInformation == null)
            {
                var isFirstUser = !await _context.UserInformation.AnyAsync();
                userInformation = new UserInformation(sso.UserSsoInformationId)
                {
                    EmailAddress = emailAddress,
                    FirstName = givenName,
                    LastName = surname,
                    UserSsoInformationIdLastLoggedIn = sso.UserSsoInformationId,
                    CreatedBy = -1,
                    CreatedDate = DateTime.Now,
                    IsSuperAdmin = isSuperAdmin || isFirstUser
                };

                _context.UserInformation.Add(userInformation);
                await _context.SaveChangesAsync();

                sso.UserInformationId = userInformation.UserInformationId;
                _context.UserSsoInformation.Update(sso);
                await _context.SaveChangesAsync();
            }
            else
            {
                userInformation.UserSsoInformationIdLastLoggedIn = sso.UserSsoInformationId;
                userInformation.IsSuperAdmin = userInformation.IsSuperAdmin || isSuperAdmin;
                _context.UserInformation.Update(userInformation);
                await _context.SaveChangesAsync();
            }

            if (string.IsNullOrEmpty(userInformation.UserInformationSqid))
            {
                userInformation.UserInformationSqid = _sqids.EncodeUserInformation(userInformation.UserInformationId);
            }

            sso.UserLastLoggedIn = DateTime.Now;
            userInformation.UserSsoInformationIdLastLoggedIn = sso.UserSsoInformationId;
            userInformation.HasCompletedRegistration = HasCompletedRegistration(userInformation);

            _context.UserSsoInformation.Update(sso);
            _context.UserInformation.Update(userInformation);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return userInformation;
        }

        /// <summary>
        /// Gets the user information asynchronously.
        /// </summary>
        /// <param name="userInformationSqid">The user information SQID.</param>
        /// <returns>The user information.</returns>
        public async Task<UserInformation> GetAsync(string userInformationSqid)
        {
            if (string.IsNullOrEmpty(userInformationSqid)) return new UserInformation();

            using var _context = _contextFactory.CreateDbContext();
            var userInformation = await _context.UserInformation.FirstOrDefaultAsync(x => x.UserInformationSqid == userInformationSqid);

            if (userInformation == null || string.IsNullOrEmpty(userInformation.UserInformationSqid))
            {
                throw new Exception($"User {userInformationSqid} not found");
            }

            return userInformation;
        }

        /// <summary>
        /// Saves the user information asynchronously.
        /// </summary>
        /// <param name="userInformation">The user information.</param>
        /// <param name="updateUserInformationSqid">The update user information SQID.</param>
        /// <returns>The updated user information.</returns>
        public async Task<UserInformation> SaveUserAsync(UserInformation userInformation, string updateUserInformationSqid)
        {
            if (string.IsNullOrEmpty(userInformation.UserInformationSqid))
                throw new ArgumentException("A profile identifier is required.", nameof(userInformation));

            var actor = await GetAsync(updateUserInformationSqid);
            if (updateUserInformationSqid != userInformation.UserInformationSqid && !actor.IsSuperAdmin)
            {
                throw new System.Security.SecurityException("Not authorized to edit this profile.");
            }
            using var _context = _contextFactory.CreateDbContext();
            var target = await _context.UserInformation.SingleAsync(x => x.UserInformationSqid == userInformation.UserInformationSqid);
            target.FirstName = userInformation.FirstName;
            target.LastName = userInformation.LastName;
            target.ContactAddress = userInformation.ContactAddress;
            target.ContactPhoneNumber = userInformation.ContactPhoneNumber;
            target.HasCompletedRegistration = HasCompletedRegistration(target);
            target.ModifiedBy = actor.UserInformationId;
            target.ModifiedDate = DateTime.Now;
            await _context.SaveChangesAsync();
            return target;
        }

        /// <summary>
        /// Determines whether the user has completed registration.
        /// </summary>
        /// <param name="userInformation">The user information.</param>
        /// <returns>
        ///   <c>true</c> if the user has completed registration; otherwise, <c>false</c>.
        /// </returns>
        public bool HasCompletedRegistration(UserInformation userInformation)
        {
            return !string.IsNullOrEmpty(userInformation.FirstName) &&
                   !string.IsNullOrEmpty(userInformation.LastName) &&
                   !string.IsNullOrEmpty(userInformation.EmailAddress) &&
                   !string.IsNullOrEmpty(userInformation.ContactPhoneNumber.Number) &&
                   !string.IsNullOrEmpty(userInformation.ContactAddress.AddressLine1) &&
                   !string.IsNullOrEmpty(userInformation.ContactAddress.Country) &&
                   !string.IsNullOrEmpty(userInformation.ContactAddress.City) &&
                   !string.IsNullOrEmpty(userInformation.ContactAddress.PostalCode) &&
                   !string.IsNullOrEmpty(userInformation.ContactAddress.State);
        }

        /// <summary>
        /// Gets the user information from the authentication state asynchronously.
        /// </summary>
        /// <param name="state">The authentication state.</param>
        /// <returns>The user information.</returns>
        public async Task<UserInformation?> GetFromStateAsync(AuthenticationState state)
        {
            if (state.User.Identity?.IsAuthenticated != true)
                return null;
            var claims = state.User.Identities.Where(identity => identity.IsAuthenticated)
                .SelectMany(identity => identity.Claims).ToList();
            var subjects = claims.Where(c => c.Type == ClaimTypes.NameIdentifier || c.Type == "sub")
                .Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();
            if (subjects.Count != 1 || string.IsNullOrWhiteSpace(subjects[0]))
                throw new System.Security.SecurityException("Authenticated identity must have exactly one subject.");
            var nameIdentifier = subjects[0];
            var givenName = claims.FirstOrDefault(c => c.Type == ClaimTypes.GivenName)?.Value ?? string.Empty;
            var surname = claims.FirstOrDefault(c => c.Type == ClaimTypes.Surname)?.Value ?? string.Empty;
            var picture = claims.FirstOrDefault(c => c.Type == "picture")?.Value ?? string.Empty;
            var emailAddress = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value ?? string.Empty;
            var emailVerified = claims.Any(c => c.Type == "email_verified" && string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase));
            var isSuperAdmin = claims.Any(c => c.Type == "https://occupancy-tracker.secure-scalable.solutions/roles" && c.Value == "SuperAdmin");

            var userInformation = await ResolveSsoAsync(nameIdentifier, givenName, surname, picture, emailAddress, emailVerified, isSuperAdmin);
            if (userInformation.IsSuperAdmin && !state.User.IsInRole("SuperAdmin"))
            {
                var claimsIdentity = state.User.Identities.FirstOrDefault(identity => identity.IsAuthenticated);
                claimsIdentity?.AddClaim(new Claim(ClaimTypes.Role, "SuperAdmin"));
            }

            return userInformation;
        }


        /// <summary>
        /// Gets the user role information asynchronously.
        /// </summary>
        /// <param name="userInformationSqid">The user information SQID.</param>
        /// <param name="forceCacheRefresh">if set to <c>true</c> [force cache refresh].</param>
        /// <returns>The list of current user role information.</returns>
        private async Task<List<CurrentUserRoleInformation>> GetUserRoleInformationAsync(string userInformationSqid, bool forceCacheRefresh = false)
        {
            using var _context = _contextFactory.CreateDbContext();
            var userId = _sqids.DecodeUserInformation(userInformationSqid);
            return await AuthorizationGrantStore.GetRolesAsync(_context, _sqids, userId);
        }

        /// <summary>
        /// Gets the user roles filtered asynchronously.
        /// </summary>
        /// <param name="userInformationSqid">The user information SQID.</param>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="locationSqid">The location SQID.</param>
        /// <returns>The list of current user role information.</returns>
        public async Task<List<CurrentUserRoleInformation>> GetUserRolesFilteredAsync(string userInformationSqid, string organizationSqid = "", string locationSqid = "")
        {
            var roles = await GetUserRoleInformationAsync(userInformationSqid);

            if (string.IsNullOrEmpty(organizationSqid) && string.IsNullOrEmpty(locationSqid))
            {
                return roles;
            }

            if (!string.IsNullOrEmpty(organizationSqid) && string.IsNullOrEmpty(locationSqid))
            {
                return roles.Where(x => x.OrganizationSqid == organizationSqid).ToList();
            }

            using var _context = _contextFactory.CreateDbContext();
            var location = await _context.Locations
                .Where(x => x.LocationSqid == locationSqid)
                .Select(x => new { x.OrganizationId })
                .SingleOrDefaultAsync();
            if (location == null) return new List<CurrentUserRoleInformation>();
            var locOrgSqid = _sqids.EncodeOrganizationId(location.OrganizationId);
            if (!string.IsNullOrEmpty(organizationSqid) && organizationSqid != locOrgSqid)
                return new List<CurrentUserRoleInformation>();
            return roles.Where(x => x.OrganizationSqid == locOrgSqid && (x.OrganizationWide || x.LocationSqid == locationSqid)).ToList();
        }

        /// <summary>
        /// Determines whether the user has access to the organization asynchronously.
        /// </summary>
        /// <param name="userInformationSqid">The user information SQID.</param>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <returns>
        ///   <c>true</c> if the user has access to the organization; otherwise, <c>false</c>.
        /// </returns>
        public async Task<bool> HasAccessToOrganizationAsync(string userInformationSqid, string organizationSqid)
        {
            var user = await GetAsync(userInformationSqid);
            return !string.IsNullOrEmpty(ResourceAuthorization.OrganizationRole(
                await GetUserRoleInformationAsync(userInformationSqid), organizationSqid, user.IsSuperAdmin));
        }

        /// <summary>
        /// Determines whether the user has access to the location asynchronously.
        /// </summary>
        /// <param name="userInformationSqid">The user information SQID.</param>
        /// <param name="locationSqid">The location SQID.</param>
        /// <returns>
        ///   <c>true</c> if the user has access to the location; otherwise, <c>false</c>.
        /// </returns>
        public async Task<bool> HasAccessToLocationAsync(string userInformationSqid, string locationSqid)
        {
            using var context = _contextFactory.CreateDbContext();
            var location = await context.Locations.Where(x => x.LocationSqid == locationSqid)
                .Select(x => new { x.OrganizationId }).SingleOrDefaultAsync();
            if (location == null) return false;
            var organizationSqid = _sqids.EncodeOrganizationId(location.OrganizationId);
            return !string.IsNullOrEmpty(await GetUserInformationRoleForLocationAsync(userInformationSqid, organizationSqid, locationSqid));
        }


        /// <summary>
        /// Gets the user information role for the organization asynchronously.
        /// </summary>
        /// <param name="userInformationSqid">The user information SQID.</param>
        /// <param name="orgSqid">The organization SQID.</param>
        /// <returns>The role name.</returns>
        public async Task<string> GetUserInformationRoleForOrganizationAsync(string userInformationSqid, string orgSqid)
        {
            var user = await GetAsync(userInformationSqid);
            return ResourceAuthorization.OrganizationRole(await GetUserRoleInformationAsync(userInformationSqid), orgSqid, user.IsSuperAdmin);
        }


        /// <summary>
        /// Gets the user information role for the location asynchronously.
        /// </summary>
        /// <param name="userInformationSqid">The user information SQID.</param>
        /// <param name="orgSqid">The organization SQID.</param>
        /// <param name="locationSqid">The location SQID.</param>
        /// <param name="forceRefreshCache">if set to <c>true</c> [force refresh cache].</param>
        /// <returns>The role name.</returns>
        public async Task<string> GetUserInformationRoleForLocationAsync(string userInformationSqid, string orgSqid, string locationSqid, bool forceRefreshCache = false)
        {
            var user = await GetAsync(userInformationSqid);
            using var context = _contextFactory.CreateDbContext();
            var location = await context.Locations.Where(x => x.LocationSqid == locationSqid)
                .Select(x => new { x.OrganizationId }).SingleOrDefaultAsync();
            if (location == null || _sqids.EncodeOrganizationId(location.OrganizationId) != orgSqid)
                return string.Empty;
            return ResourceAuthorization.LocationRole(await GetUserRoleInformationAsync(userInformationSqid), orgSqid, locationSqid, user.IsSuperAdmin);
        }

        /// <summary>
        /// Logs the access exception asynchronously.
        /// </summary>
        /// <param name="userInformationSqid">The user information SQID.</param>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="locationSqid">The location SQID.</param>
        /// <param name="entranceSqid">The entrance SQID.</param>
        /// <param name="ipAddress">The IP address.</param>
        /// <param name="detailedMessage">The detailed message.</param>
        /// <param name="userMessage">The user message.</param>
        /// <returns>The reference ID of the logged access exception.</returns>
        public async Task<string> LogAccessExceptionAsync(string userInformationSqid, string organizationSqid, string locationSqid, string entranceSqid, string ipAddress, string detailedMessage, string userMessage)
        {
            var user = await GetAsync(userInformationSqid);
            Organization? logOrg = null;
            Location? logLoc = null;
            Entrance? logEnt = null;

            if (!string.IsNullOrEmpty(organizationSqid))
            {
                using (var _dbContext = _contextFactory.CreateDbContext())
                {
                    logOrg = await _dbContext.Organizations.FirstOrDefaultAsync(x => x.OrganizationSqid == organizationSqid);
                }
            }

            if (!string.IsNullOrEmpty(locationSqid))
            {
                using (var _dbContext = _contextFactory.CreateDbContext())
                {
                    logLoc = await _dbContext.Locations.FirstOrDefaultAsync(x => x.LocationSqid == locationSqid);
                }
            }

            if (!string.IsNullOrEmpty(entranceSqid))
            {
                using (var _dbContext = _contextFactory.CreateDbContext())
                {
                    logEnt = await _dbContext.Entrances.FirstOrDefaultAsync(x => x.EntranceSqid == entranceSqid);
                }
            }

            var invalidSecurityAttempt = new InvalidSecurityAttempt
            {
                UserInformationId = user.UserInformationId,
                OrganizationId = logOrg?.OrganizationId,
                LocationId = logLoc?.LocationId,
                EntranceId = logEnt?.EntranceId,
                IpAddress = ipAddress,
                AdditionalAttemptInformation = detailedMessage,
                AttemptLogged = DateTime.Now
            };

            using var _context = _contextFactory.CreateDbContext();
            _context.InvalidSecurityAttempt.Add(invalidSecurityAttempt);
            await _context.SaveChangesAsync();

            invalidSecurityAttempt.InvalidSecurityAttemptSqid = _sqids.EncodeInvalidSecurityAttempt(invalidSecurityAttempt.InvalidSecurityAttemptId);
            await _context.SaveChangesAsync();

            throw new System.Security.SecurityException($"{userMessage}{Environment.NewLine}ReferenceID: {invalidSecurityAttempt.InvalidSecurityAttemptSqid}");
        }

        /// <summary>
        /// Determines whether the user is a location admin asynchronously.
        /// </summary>
        /// <param name="userInformationSqid"></param>
        /// <param name="orgSqid"></param>
        /// <param name="locationSqid"></param>
        /// <param name="forceCacheRefresh"></param>
        /// <returns></returns>
        public async Task<bool> IsLocAdminAsync(string userInformationSqid, string orgSqid, string locationSqid, bool forceCacheRefresh = false)
        {
            var locRole = await GetUserInformationRoleForLocationAsync(userInformationSqid, orgSqid, locationSqid);
            return locRole == AuthorizationRecords.Roles.OrganizationAdmin.Name ||
                   locRole == AuthorizationRecords.Roles.SuperAdmin.Name ||
                   locRole == AuthorizationRecords.Roles.LocationAdmin.Name;
        }

        /// <summary>
        /// Determines whether the user is an organization admin asynchronously.
        /// </summary>
        /// <param name="userInformationSqid"></param>
        /// <param name="organizationSqid"></param>
        /// <param name="forceCacheRefresh"></param>
        /// <returns></returns>
        public async Task<bool> IsOrgAdminAsync(string userInformationSqid, string organizationSqid, bool forceCacheRefresh = false)
        {
            var orgRole = await GetUserInformationRoleForOrganizationAsync(userInformationSqid, organizationSqid);
            return orgRole == AuthorizationRecords.Roles.OrganizationAdmin.Name || orgRole == AuthorizationRecords.Roles.SuperAdmin.Name;
        }
    }
}
