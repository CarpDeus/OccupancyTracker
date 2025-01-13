using Enyim.Caching;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.IService;
using OccupancyTracker.Models;
using Serilog;

//using OccupancyTracker.Models.Migrations;
using Sqids;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace OccupancyTracker.Service
{

    /// <summary>
    /// Service class for managing organizations.
    /// </summary>
    public class OrganizationService : IOrganizationService
    {
        private readonly IDbContextFactory<OccupancyContext> _contextFactory;
        private readonly IMemcachedClient _memcachedClient;
        private readonly IOccAuthorizationService _authorizationService;
        private readonly ISqidsEncoderFactory _organizationSqidsEncoderFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="OrganizationService"/> class.
        /// </summary>
        /// <param name="contextFactory">The context factory.</param>
        /// <param name="organizationSqidsEncoderFactory">The organization SQIDs encoder factory.</param>
        /// <param name="memcachedClient">The memcached client.</param>
        /// <param name="authorizationService">The authorization service.</param>
        public OrganizationService(IDbContextFactory<OccupancyContext> contextFactory, ISqidsEncoderFactory organizationSqidsEncoderFactory, IMemcachedClient memcachedClient, IOccAuthorizationService authorizationService)
        {
            _authorizationService = authorizationService;
            _memcachedClient = memcachedClient;
            _contextFactory = contextFactory;
            _organizationSqidsEncoderFactory = organizationSqidsEncoderFactory;
        }

        /// <summary>
        /// Changes the status of an organization.
        /// </summary>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="fromStatus">The current status.</param>
        /// <param name="toStatus">The new status.</param>
        /// <param name="userInformation">The user information.</param>
        /// <returns>The updated organization.</returns>
        public async Task<Organization?> ChangeStatusAsync(string organizationSqid, int fromStatus, int toStatus, UserInformation userInformation)
        {
            string userInformationSqid = userInformation.UserInformationSqid;
            if (!await _authorizationService.IsOrgAdminAsync(userInformationSqid, organizationSqid))
            {
                await _authorizationService.LogAccessExceptionAsync(userInformationSqid, organizationSqid, "", "", "", $"User does not have rights to update organization {organizationSqid}", $"User does not have rights to update organization {organizationSqid}");
                return null;
            }

            var org = await GetAsync(organizationSqid, userInformation, true);
            if (org == null)
            {
                throw new KeyNotFoundException($"Organization {organizationSqid} was not found");
            }

            if (org.CurrentStatus == 2 && !userInformation.IsSuperAdmin)
            {
                throw new KeyNotFoundException($"Organization {organizationSqid} was not found");
            }

            int[] validStates = Statuses.DataStatus.ValidChangeStates(fromStatus);
            if (!validStates.Contains(toStatus))
            {
                StringBuilder sb = new StringBuilder("Organization is not in the correct CurrentStatus to perform this operation. ");
                sb.Append($"{organizationSqid} is currently {org.CurrentStatusDescription} so it can only change to:");
                for (int i = 0; i < validStates.Length; i++)
                {
                    sb.Append($"{Statuses.DataStatus.FromId(validStates[i]).Name}");
                    if (i != validStates.Length - 1) sb.Append(", ");
                }
                throw new InvalidOperationException(sb.ToString());
            }

            using (var _context = _contextFactory.CreateDbContext())
            {
                if (org.CurrentStatus == fromStatus)
                {
                    org.CurrentStatus = toStatus;
                    await InternalSaveAsync(org, userInformation);
                }
                else
                {
                    throw new InvalidOperationException($"Organization status not changed.");
                }
            }

            await GetListAsync(userInformation, "", true);
            return await GetAsync(organizationSqid, userInformation, true);
        }

        /// <summary>
        /// Gets the list of active organizations.
        /// </summary>
        /// <param name="userInformation">The user information.</param>
        /// <param name="filter">The filter criteria.</param>
        /// <param name="forceCacheRefresh">if set to <c>true</c> forces cache refresh.</param>
        /// <returns>The list of active organizations.</returns>
        public async Task<List<Organization>> GetActiveListAsync(UserInformation userInformation, string filter, bool forceCacheRefresh = false)
        {
            var organizations = await GetListAsync(userInformation, filter, forceCacheRefresh);
            return organizations.Where(x => x.CurrentStatus == 0).ToList();
        }

        /// <summary>
        /// Gets an organization by its SQID.
        /// </summary>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="userInformation">The user information.</param>
        /// <param name="forceCacheRefresh">if set to <c>true</c> forces cache refresh.</param>
        /// <returns>The organization.</returns>
        public async Task<Organization?> GetAsync(string organizationSqid, UserInformation userInformation, bool forceCacheRefresh = false)
        {
            string userInformationSqid = userInformation.UserInformationSqid;
            if (!await _authorizationService.HasAccessToOrganizationAsync(userInformationSqid, organizationSqid))
            {
                await _authorizationService.LogAccessExceptionAsync(userInformationSqid, organizationSqid, "", "", "", $"User does not have access to organization {organizationSqid}", $"User does not have access to organization {organizationSqid}");
                return null;
            }

            using (var _context = _contextFactory.CreateDbContext())
            {
                var org = await _context.Organizations.FirstOrDefaultAsync(x => x.OrganizationSqid == organizationSqid);
                if (org == null)
                {
                    throw new InvalidOperationException("Organization not found for organizationSqid " + organizationSqid);
                }
                return org;
            }
        }

        /// <summary>
        /// Gets an organization by its location SQID.
        /// </summary>
        /// <param name="locationSqid">The location SQID.</param>
        /// <param name="userInformation">The user information.</param>
        /// <param name="forceCacheRefresh">if set to <c>true</c> forces cache refresh.</param>
        /// <returns>The organization.</returns>
        public async Task<Organization?> GetByLocationAsync(string locationSqid, UserInformation userInformation, bool forceCacheRefresh = false)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets the list of deleted organizations.
        /// </summary>
        /// <param name="userInformation">The user information.</param>
        /// <param name="filter">The filter criteria.</param>
        /// <param name="forceCacheRefresh">if set to <c>true</c> forces cache refresh.</param>
        /// <returns>The list of deleted organizations.</returns>
        public async Task<List<Organization>> GetDeletedListAsync(UserInformation userInformation, string filter, bool forceCacheRefresh = false)
        {
            var organizations = await GetListAsync(userInformation, filter, forceCacheRefresh);
            return organizations.Where(x => x.CurrentStatus == 1).ToList();
        }

        /// <summary>
        /// Gets the list of organizations.
        /// </summary>
        /// <param name="userInformation">The user information.</param>
        /// <param name="filter">The filter criteria.</param>
        /// <param name="forceCacheRefresh">if set to <c>true</c> forces cache refresh.</param>
        /// <returns>The list of organizations.</returns>
        public async Task<List<Organization>> GetListAsync(UserInformation userInformation, string filter = "", bool forceCacheRefresh = false)
        {
            return (await InternalGetListAsync(userInformation, forceCacheRefresh)).Where(x => x.FilterCriteria(filter)).ToList();
        }

        /// <summary>
        /// Gets the list of permanently deleted organizations.
        /// </summary>
        /// <param name="userInformation">The user information.</param>
        /// <param name="filter">The filter criteria.</param>
        /// <param name="forceCacheRefresh">if set to <c>true</c> forces cache refresh.</param>
        /// <returns>The list of permanently deleted organizations.</returns>
        public async Task<List<Organization>> GetPermanentlyDeletedListAsync(UserInformation userInformation, string filter, bool forceCacheRefresh = false)
        {
            if (userInformation.IsSuperAdmin)
                return (await GetListAsync(userInformation, filter, forceCacheRefresh)).Where(x => x.CurrentStatus == 2).ToList();
            else return new List<Organization>();
        }

        /// <summary>
        /// Determines whether the organization name is unique.
        /// </summary>
        /// <param name="organizationName">Name of the organization.</param>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <returns><c>true</c> if the organization name is unique; otherwise, <c>false</c>.</returns>
        public async Task<bool> IsUniqueNameAsync(string organizationName, string organizationSqid)
        {
            using (var _context = _contextFactory.CreateDbContext())
            {
                return !await _context.Organizations.AnyAsync(x => x.OrganizationName == organizationName && x.OrganizationSqid != organizationSqid);
            }
        }

        /// <summary>
        /// Saves the organization.
        /// </summary>
        /// <param name="org">The organization.</param>
        /// <param name="userInformation">The user information.</param>
        /// <returns>The saved organization.</returns>
        public async Task<Organization?> SaveAsync(Organization org, UserInformation userInformation)
        {
            string userInformationSqid = userInformation.UserInformationSqid;
            if (string.IsNullOrEmpty(org.OrganizationSqid) && org.OrganizationId == 0)
            {
                await _memcachedClient.RemoveAsync($"{userInformationSqid}:Orgs");
                using (var _context = _contextFactory.CreateDbContext())
                {
                    var newOrg = await InternalSaveAsync(org, userInformation);
                    var organizationUser = new OrganizationUser
                    {
                        OrganizationId = newOrg.OrganizationId,
                        UserInformationId = userInformation.UserInformationId,
                        CreatedBy = userInformationSqid,
                        CreatedDate = DateTime.UtcNow
                    };
                    _context.OrganizationUsers.Add(organizationUser);
                    await _context.SaveChangesAsync();

                    _context.OrganizationUserRoles.Add(new OrganizationUserRole
                    {
                        OrganizationUserId = organizationUser.OrganizationUsersId,
                        RoleName = AuthorizationRecords.Roles.OrganizationAdmin.Name,
                        OrganizationWide = true
                    });
                    await _context.SaveChangesAsync();
                    userInformation.BelongsToOrganization = true;
                    _context.UserInformation.Update(userInformation);
                    _context.Entry(userInformation).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
            }
            else
            {
                if (!await _authorizationService.HasAccessToOrganizationAsync(userInformationSqid, org.OrganizationSqid))
                {
                    throw new InvalidOperationException($"User {userInformationSqid} has no rights to the organization");
                }
                if (!await _authorizationService.IsOrgAdminAsync(userInformationSqid, org.OrganizationSqid))
                {
                    throw new InvalidOperationException($"User {userInformationSqid} does not have rights to update organization");
                }
                using (var _context = _contextFactory.CreateDbContext())
                {
                    org.ModifiedBy = userInformationSqid;
                    org.ModifiedDate = DateTime.Now;
                    await InternalSaveAsync(org, userInformation);
                }
            }
            await _memcachedClient.RemoveAsync($"{userInformationSqid}:Roles");
            await GetListAsync(userInformation, "", true);
            return org;
        }

        /// <summary>
        /// Internally saves the organization.
        /// </summary>
        /// <param name="org">The organization.</param>
        /// <param name="userInformation">The user information.</param>
        /// <returns>The saved organization.</returns>
        private async Task<Organization> InternalSaveAsync(Organization org, UserInformation userInformation)
        {
            using (var _context = _contextFactory.CreateDbContext())
            {
                _context.ChangeTracker.Clear();
                if (org.OrganizationId == 0)
                {
                    await _context.Organizations.AddAsync(org);
                    await _context.SaveChangesAsync();
                    org.OrganizationSqid = _organizationSqidsEncoderFactory.EncodeOrganizationId(org.OrganizationId);
                }
                else
                {
                    if (string.IsNullOrEmpty(org.OrganizationSqid))
                    {
                        org.OrganizationSqid = _organizationSqidsEncoderFactory.EncodeOrganizationId(org.OrganizationId);
                    }
                    _context.Organizations.Update(org);
                }
                await _context.SaveChangesAsync();
            }
            return org;
        }

        /// <summary>
        /// Internally gets the list of organizations.
        /// </summary>
        /// <param name="userInformation">The user information.</param>
        /// <param name="forceCacheRefresh">if set to <c>true</c> forces cache refresh.</param>
        /// <returns>The list of organizations.</returns>
        private async Task<List<Organization>> InternalGetListAsync(UserInformation userInformation, bool forceCacheRefresh = false)
        {
            string userInformationSqid = userInformation.UserInformationSqid;
            List<Organization> orgs = new();

            if (userInformation.IsSuperAdmin)
            {
                using (var _context = _contextFactory.CreateDbContext())
                {
                    orgs = await _context.Organizations.ToListAsync();
                }
            }
            else
            {
                if (orgs == null || orgs.Count == 0 || forceCacheRefresh)
                {
                    using (var _context = _contextFactory.CreateDbContext())
                    {
                        var userOrgIds = await _context.OrganizationUsers
                            .Where(x => x.UserInformationId == userInformation.UserInformationId)
                            .Select(x => x.OrganizationId)
                            .ToListAsync();

                        var adminOrgIds = (await _authorizationService.GetUserRolesFilteredAsync(userInformationSqid))
                            .Where(role => role.RoleName == "OrganizationAdmin")
                            .Select(role => role.OrganizationSqid)
                            .ToList();

                        orgs = await _context.Organizations
                            .Where(x => ((x.CurrentStatus == 0 && userOrgIds.Contains(x.OrganizationId)) ||
                                        (x.CurrentStatus == 1 && adminOrgIds.Contains(x.OrganizationSqid)))
                                        && x.CurrentStatus != 2)
                            .ToListAsync();
                    }
                }
            }
            return orgs;
        }
    }
}
