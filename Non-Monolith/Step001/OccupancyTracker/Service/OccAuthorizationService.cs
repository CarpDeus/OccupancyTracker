using Enyim.Caching;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using OccupancyTracker.Data.Models;
using OccupancyTracker.IService;
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
        private async Task<UserInformation> ResolveSsoAsync(string auth0Identifier, string givenName, string surname, string picture, string emailAddress, bool isSuperAdmin)
        {
            using var _context = _contextFactory.CreateDbContext();
            var sso = await _context.UserSsoInformation.FirstOrDefaultAsync(x => x.Auth0Identifier == auth0Identifier);
            var userInformation = await _context.UserInformation.FirstOrDefaultAsync(x => x.EmailAddress == emailAddress);

            if (sso == null)
            {
                sso = new UserSsoInformation
                {
                    Auth0Identifier = auth0Identifier,
                    GivenName = givenName,
                    Surname = surname,
                    Picture = picture,
                    EmailAddress = emailAddress,
                    UserInformationId = userInformation?.UserInformationId ?? -1
                };

                _context.UserSsoInformation.Add(sso);
                await _context.SaveChangesAsync();
            }

            if (userInformation == null)
            {
                userInformation = new UserInformation(sso.UserSsoInformationId)
                {
                    EmailAddress = emailAddress,
                    FirstName = givenName,
                    LastName = surname,
                    UserSsoInformationIdLastLoggedIn = sso.UserSsoInformationId,
                    CreatedBy = -1,
                    CreatedDate = DateTime.Now,
                    IsSuperAdmin = isSuperAdmin
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
                userInformation.IsSuperAdmin = isSuperAdmin;
                _context.UserInformation.Update(userInformation);
                await _context.SaveChangesAsync();
            }

            if (string.IsNullOrEmpty(userInformation.UserInformationSqid))
            {
                userInformation.UserInformationSqid = _sqids.EncodeUserInformation(userInformation.UserInformationId);
            }

            sso.UserLastLoggedIn = DateTime.Now;
            userInformation.UserSsoInformationIdLastLoggedIn = sso.UserSsoInformationId;
            userInformation.HasCompletedRegistration = HasUserCompletedProfile(
                userInformation.ContactAddress.AddressLine1,
                userInformation.ContactAddress.City,
                userInformation.ContactAddress.State,
                userInformation.ContactAddress.PostalCode,
                userInformation.ContactAddress.State,
                userInformation.ContactPhoneNumber.Number,
                userInformation.ContactPhoneNumber.CountryCode,
                userInformation.FirstName,
                userInformation.LastName
            );

            _context.UserSsoInformation.Update(sso);
            _context.UserInformation.Update(userInformation);
            await _context.SaveChangesAsync();

            return userInformation;
        }

        /// <summary>
        /// Determines whether the user has completed their profile.
        /// </summary>
        /// <param name="userFields">The user fields.</param>
        /// <returns>
        ///   <c>true</c> if the user has completed their profile; otherwise, <c>false</c>.
        /// </returns>
        private bool HasUserCompletedProfile(params string[] userFields)
        {
            return userFields.All(field => !string.IsNullOrEmpty(field));
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
            {
                userInformation.UserInformationSqid = _sqids.EncodeUserInformation(userInformation.UserInformationId);
            }

            long modifiedBy = userInformation.UserInformationId;
            if (updateUserInformationSqid != userInformation.UserInformationSqid)
            {
                var updateUser = await GetAsync(updateUserInformationSqid);
                if (!updateUser.IsSuperAdmin)
                {
                    throw new Exception($"User {updateUserInformationSqid} is not authorized");
                }
                modifiedBy = updateUser.UserInformationId;
            }

            userInformation.HasCompletedRegistration = HasCompletedRegistration(userInformation);
            userInformation.ModifiedBy = modifiedBy;
            userInformation.ModifiedDate = DateTime.Now;

            using var _context = _contextFactory.CreateDbContext();
            _context.UserInformation.Update(userInformation);
            await _context.SaveChangesAsync();

            return userInformation;
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
            var claims = state.User.Claims.ToList();
            var nameIdentifier = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
            var givenName = claims.FirstOrDefault(c => c.Type == ClaimTypes.GivenName)?.Value ?? string.Empty;
            var surname = claims.FirstOrDefault(c => c.Type == ClaimTypes.Surname)?.Value ?? string.Empty;
            var picture = claims.FirstOrDefault(c => c.Type == "picture")?.Value ?? string.Empty;
            var emailAddress = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value ?? string.Empty;
            var isSuperAdmin = claims.Any(c => c.Type == "https://occupancy-tracker.secure-scalable.solutions/roles" && c.Value == "SuperAdmin");

            if (isSuperAdmin && !state.User.IsInRole("SuperAdmin"))
            {
                var claimsIdentity = (ClaimsIdentity)state.User.Identity;
                claimsIdentity.AddClaim(new Claim(ClaimTypes.Role, "SuperAdmin"));
            }

            return await ResolveSsoAsync(nameIdentifier, givenName, surname, picture, emailAddress, isSuperAdmin);
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
            return await _context.OrganizationUserRoles
                .Where(o => _context.OrganizationUsers.Any(x => x.UserInformationId == _sqids.DecodeUserInformation(userInformationSqid) && o.OrganizationUserId == x.OrganizationUsersId))
                .Select(x => new CurrentUserRoleInformation
                {
                    OrganizationSqid = _sqids.EncodeOrganizationId(_context.OrganizationUsers.FirstOrDefault(u => u.OrganizationUsersId == x.OrganizationUserId).OrganizationId),
                    LocationSqid = x.Location.LocationSqid,
                    OrganizationWide = x.OrganizationWide,
                    RoleName = x.RoleName
                }).ToListAsync();
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
            var locOrgSqid = _sqids.EncodeOrganizationId(
                await _context.Locations
                    .Where(x => x.LocationSqid == locationSqid)
                    .Select(x => x.OrganizationId)
                    .FirstOrDefaultAsync()
            );

            return roles.Where(x => x.OrganizationSqid == locOrgSqid && x.OrganizationWide || x.LocationSqid == locationSqid).ToList();
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
            return user.IsSuperAdmin || (await GetUserRoleInformationAsync(userInformationSqid)).Any(x => x.OrganizationSqid == organizationSqid);
        }

        /// <summary>
        /// Gets the user location list asynchronously.
        /// </summary>
        /// <param name="userInformationSqid">The user information SQID.</param>
        /// <param name="forceCacheRefresh">if set to <c>true</c> [force cache refresh].</param>
        /// <returns>The list of user location SQIDs.</returns>
        private async Task<List<string>> GetUserLocationListAsync(string userInformationSqid, bool forceCacheRefresh = false)
        {
            using var _context = _contextFactory.CreateDbContext();
            return await _context.Organizations
                .Where(o => _context.OrganizationUsers
                    .Where(x => x.UserInformationId == _sqids.DecodeUserInformation(userInformationSqid))
                    .Select(x => x.OrganizationId)
                    .Contains(o.OrganizationId))
                .Select(o => o.OrganizationSqid)
                .ToListAsync();
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
            var pos = _sqids.DecodeSqids(null, locationSqid);
            return (await GetAsync(userInformationSqid)).IsSuperAdmin ||
                   await IsOrgAdminAsync(userInformationSqid, pos.OrganizationSqid) ||
                     (await GetUserRoleInformationAsync(userInformationSqid)).Any(x => x.OrganizationWide && x.RoleName== AuthorizationRecords.Roles.User.Name) ||
                   (await GetUserLocationListAsync(userInformationSqid, true)).Any(x => x.Equals(locationSqid));
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
            if (user.IsSuperAdmin) return AuthorizationRecords.Roles.SuperAdmin.Name;

            if (!await HasAccessToOrganizationAsync(userInformationSqid, orgSqid)) return string.Empty;

            var roles = (await GetUserRoleInformationAsync(userInformationSqid)).Where(x => x.OrganizationSqid == orgSqid).ToList();

            if (!roles.Any()) return AuthorizationRecords.Roles.User.Name;

            if (roles.Any(x => x.RoleName == AuthorizationRecords.Roles.OrganizationAdmin.Name)) return AuthorizationRecords.Roles.OrganizationAdmin.Name;
            if (roles.Any(x => x.RoleName == AuthorizationRecords.Roles.LocationAdmin.Name && x.OrganizationWide)) return AuthorizationRecords.Roles.LocationAdmin.Name;

            return AuthorizationRecords.Roles.User.Name;
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
            if (user.IsSuperAdmin) return AuthorizationRecords.Roles.SuperAdmin.Name;

            if (!await HasAccessToOrganizationAsync(userInformationSqid, orgSqid)) return string.Empty;

            var roles = (await GetUserRoleInformationAsync(userInformationSqid)).Where(x => x.OrganizationSqid == orgSqid).ToList();

            if (!roles.Any()) return AuthorizationRecords.Roles.User.Name;

            if (roles.Any(x => x.RoleName == AuthorizationRecords.Roles.OrganizationAdmin.Name)) return AuthorizationRecords.Roles.OrganizationAdmin.Name;
            if (roles.Any(x => x.RoleName == AuthorizationRecords.Roles.LocationAdmin.Name && x.OrganizationWide)) return AuthorizationRecords.Roles.LocationAdmin.Name;
            if (roles.Any(x => x.RoleName == AuthorizationRecords.Roles.LocationAdmin.Name && x.LocationSqid == locationSqid)) return AuthorizationRecords.Roles.LocationAdmin.Name;

            return AuthorizationRecords.Roles.User.Name;
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
