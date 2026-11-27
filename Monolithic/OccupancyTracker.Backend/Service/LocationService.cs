using OccupancyTracker.IRepository;
using Microsoft.Identity.Client;
using OccupancyTracker.IService;
using OccupancyTracker.Models;
using Serilog;

//using OccupancyTracker.Models.Migrations;
using Sqids;
using System.Collections.Generic;
using System.Text;

namespace OccupancyTracker.Service
{
    public class LocationService : ILocationService
    {
        private readonly IRepositorySessionFactory _repositoryFactory;
        private readonly ISqidsEncoderFactory _sqids;
        private readonly ICacheRepository _cacheRepository;
        private readonly IOccAuthorizationService _authorizationService;

        public LocationService(IRepositorySessionFactory repositoryFactory, ISqidsEncoderFactory sqidsEncoderFactory, ICacheRepository cacheRepository,
            IOccAuthorizationService authorizationService)
        {
            _authorizationService = authorizationService;
            _cacheRepository = cacheRepository;
            _repositoryFactory = repositoryFactory;
            _sqids = sqidsEncoderFactory;
        }

        /// <summary>
        /// Changes the status of a location.
        /// </summary>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="locationSqid">The location SQID.</param>
        /// <param name="fromStatus">The current status of the location.</param>
        /// <param name="toStatus">The new status of the location.</param>
        /// <param name="userInformation">The user information.</param>
        /// <returns>The updated location.</returns>
        /// <exception cref="Exception">Thrown when the organization is not found or the user does not have rights to update the location.</exception>
        public async Task<Location?> ChangeStatusAsync(string organizationSqid, string locationSqid, int fromStatus, int toStatus, UserInformation userInformation)
        {
            if (!(await OrganizationIsValidForUser(organizationSqid, userInformation)))
            {
                throw new Exception("Organization not found");
            }
            string userInformationSqid = userInformation.UserInformationSqid;
            if (!(await _authorizationService.IsLocAdminAsync(userInformationSqid, organizationSqid, locationSqid)))
            {
                await _authorizationService.LogAccessExceptionAsync(userInformationSqid, organizationSqid, locationSqid, "", "", $"User does not have rights to update location {locationSqid}", $"User does not have rights to update location {locationSqid}");
            }
            var loc = await GetAsync(organizationSqid, locationSqid, userInformation, true);
            if (loc == null)
            {
                throw new KeyNotFoundException($"Location {locationSqid} was not found");
            }
            if (loc.CurrentStatus == 2 && !userInformation.IsSuperAdmin)
            {
                throw new KeyNotFoundException($"Location {locationSqid}  was not found");
            }
            int[] validStates = Statuses.DataStatus.ValidChangeStates(fromStatus);
            if (!validStates.Contains(toStatus))
            {
                StringBuilder sb = new StringBuilder("Location is not in the correct CurrentStatus to perform this operation. ");
                sb.Append($"{locationSqid} is currently {loc.CurrentStatusDescription} so it can only change to:");
                for (int i = 0; i < validStates.Length; i++)
                {
                    sb.Append("${Statuses.DataStatus.FromId(i).Name}");
                    if (i != validStates.Length - 1) sb.Append(", ");
                }
                throw new InvalidOperationException(sb.ToString());
            }
            using (var _context = _repositoryFactory.Create())
            {
                if (loc != null)
                {
                    if (loc.CurrentStatus == fromStatus)
                    {
                        loc.CurrentStatus = toStatus;
                        await InternalSaveAsync(loc, organizationSqid, userInformation);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Loction status not changed.");
                    }
                }
            }
            // Clear cached items
            await GetListAsync(userInformation, organizationSqid, "", true);
            return await GetAsync(organizationSqid, locationSqid, userInformation, true);
        }

        /// <summary>
        /// Gets a list of active locations.
        /// </summary>
        /// <param name="userInformation">The user information.</param>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="filter">The filter to apply.</param>
        /// <param name="forceCacheRefresh">Whether to force a cache refresh.</param>
        /// <returns>A list of active locations.</returns>
        public async Task<List<Location>> GetActiveListAsync(UserInformation userInformation, string organizationSqid, string filter, bool forceCacheRefresh = false)
        {
            if (userInformation.IsSuperAdmin)
            {
                return (await GetFullListAsync(userInformation, organizationSqid, filter, forceCacheRefresh)).Where(e => e.CurrentStatus == 0).ToList();
            }
            else return (await GetListAsync(userInformation, organizationSqid, filter, forceCacheRefresh)).Where(e => e.CurrentStatus == 0).ToList();
        }

        /// <summary>
        /// Gets a location by its SQID.
        /// </summary>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="locationSqid">The location SQID.</param>
        /// <param name="userInformation">The user information.</param>
        /// <param name="forceCacheRefresh">Whether to force a cache refresh.</param>
        /// <returns>The location.</returns>
        /// <exception cref="Exception">Thrown when the organization is not found or the location is not found.</exception>
        public async Task<Location?> GetAsync(string organizationSqid, string locationSqid, UserInformation userInformation, bool forceCacheRefresh = false)
        {
            if (!(await OrganizationIsValidForUser(organizationSqid, userInformation)))
            {
                throw new Exception("Organization not found");
            }
            string cacheKey = $"Loc:{locationSqid}";
            Location? loc = null;
            if (loc == null || forceCacheRefresh)
            {
                using (var context = _repositoryFactory.Create())
                {
                    loc = await context.Locations.FirstOrDefaultAsync(e => e.LocationSqid == locationSqid);
                    if (loc != null)
                        await _cacheRepository.WriteAsync(cacheKey, loc, TimeSpan.FromSeconds(15));
                    else
                    {
                        throw new InvalidOperationException("Location not found for sqid " + locationSqid);
                    }
                }
            }
            return loc;
        }

        /// <summary>
        /// Gets a list of deleted locations.
        /// </summary>
        /// <param name="userInformation">The user information.</param>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="filter">The filter to apply.</param>
        /// <param name="forceCacheRefresh">Whether to force a cache refresh.</param>
        /// <returns>A list of deleted locations.</returns>
        public async Task<List<Location>> GetDeletedListAsync(UserInformation userInformation, string organizationSqid, string filter, bool forceCacheRefresh = false)
        {
            if (userInformation.IsSuperAdmin)
            {
                return (await GetFullListAsync(userInformation, organizationSqid, filter, forceCacheRefresh)).Where(e => e.CurrentStatus == 1).ToList();
            }
            else return (await GetListAsync(userInformation, organizationSqid, filter, forceCacheRefresh)).Where(e => e.CurrentStatus == 1).ToList();
        }

        /// <summary>
        /// Gets a full list of locations.
        /// </summary>
        /// <param name="userInformation">The user information.</param>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="filter">The filter to apply.</param>
        /// <param name="forceCacheRefresh">Whether to force a cache refresh.</param>
        /// <returns>A full list of locations.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the location list is not found.</exception>
        public async Task<List<Location>> GetFullListAsync(UserInformation userInformation, string organizationSqid, string filter = "", bool forceCacheRefresh = false)
        {
            if (!userInformation.IsSuperAdmin) return new List<Location>();
            string cacheKey = $"LocList:{organizationSqid}:{userInformation.UserInformationSqid}";
            List<Location> retVal = new();
            ParsedOrganizationSqids pos = _sqids.DecodeSqids(organizationSqid);
            //if ( retVal == null || retVal.Count==0 || forceCacheRefresh)
            {
                using var session = _repositoryFactory.Create();
                retVal = await session.Locations.ListAsync(e => e.OrganizationId == (long)pos.OrganizationId);
                if (retVal != null)
                    await _cacheRepository.WriteAsync(cacheKey, retVal, TimeSpan.FromSeconds(15));
                else
                {
                    throw new InvalidOperationException("Location List not found for OganizationSqid " + organizationSqid);
                }
            }
            return retVal.Where(x => x.FilterCriteria(filter)).ToList();
        }

        /// <summary>
        /// Gets a list of locations.
        /// </summary>
        /// <param name="userInformation">The user information.</param>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="filter">The filter to apply.</param>
        /// <param name="forceCacheRefresh">Whether to force a cache refresh.</param>
        /// <returns>A list of locations.</returns>
        /// <exception cref="Exception">Thrown when the organization is not found or the location list is not found.</exception>
        public async Task<List<Location>> GetListAsync(UserInformation userInformation, string organizationSqid, string filter = "", bool forceCacheRefresh = false)
        {
            if (!(await OrganizationIsValidForUser(organizationSqid, userInformation)))
            {
                throw new Exception("Organization not found");
            }
            string cacheKey = $"LocList:{organizationSqid}:{userInformation.UserInformationSqid}";
            List<Location> retVal = new();
            ParsedOrganizationSqids pos = _sqids.DecodeSqids(organizationSqid);
            //if (retVal == null || retVal.Count==0|| forceCacheRefresh)
            {
                bool isOrgAdmin = await _authorizationService.IsOrgAdminAsync(userInformation.UserInformationSqid, organizationSqid);
                using var session = _repositoryFactory.Create();
                retVal = await session.Locations.ListAsync(e => e.OrganizationId == (long)pos.OrganizationId
                    && (e.CurrentStatus == 0 || (isOrgAdmin && e.CurrentStatus == 1)));
                if (retVal != null)
                    await _cacheRepository.WriteAsync(cacheKey, retVal, TimeSpan.FromSeconds(15));
                else
                {
                    throw new InvalidOperationException("Location List not found for OganizationSqid " + organizationSqid);
                }
            }
            return retVal.Where(x => x.FilterCriteria(filter)).ToList();
        }

        /// <summary>
        /// Gets a list of permanently deleted locations.
        /// </summary>
        /// <param name="userInformation">The user information.</param>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="filter">The filter to apply.</param>
        /// <param name="forceCacheRefresh">Whether to force a cache refresh.</param>
        /// <returns>A list of permanently deleted locations.</returns>
        public async Task<List<Location>> GetPermanentlyDeletedListAsync(UserInformation userInformation, string organizationSqid, string filter, bool forceCacheRefresh = false)
        {
            return (await GetFullListAsync(userInformation, organizationSqid, filter, forceCacheRefresh)).Where(x => x.CurrentStatus == 2).ToList();
        }

        /// <summary>
        /// Saves a location.
        /// </summary>
        /// <param name="location">The location to save.</param>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="userInformation">The user information.</param>
        /// <returns>The saved location.</returns>
        public async Task<Location> SaveAsync(Location location, string organizationSqid, UserInformation userInformation)
        {
            return await InternalSaveAsync(location, organizationSqid, userInformation);
        }

        /// <summary>
        /// Internally saves a location.
        /// </summary>
        /// <param name="loc">The location to save.</param>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="userInformation">The user information.</param>
        /// <returns>The saved location.</returns>
        /// <exception cref="Exception">Thrown when the organization is not found.</exception>
        private async Task<Location> InternalSaveAsync(Location loc, string organizationSqid, UserInformation userInformation)
        {
            if (!(await OrganizationIsValidForUser(organizationSqid, userInformation)))
            {
                throw new Exception("Organization not found");
            }
            using (var _context = _repositoryFactory.Create())
            {
                if (loc.LocationId == 0)
                {
                    _context.Locations.Add(loc);
                    await _context.SaveChangesAsync();
                    loc.LocationSqid = _sqids.EncodeLocationId(loc.OrganizationId, loc.LocationId);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    if (string.IsNullOrEmpty(loc.LocationSqid))
                    {
                        loc.LocationSqid = _sqids.EncodeLocationId(loc.OrganizationId, loc.LocationId);
                    }
                    _context.Locations.Update(loc);
                    await _context.SaveChangesAsync();
                }
                await _context.SaveChangesAsync();
            }

            return await GetAsync(organizationSqid, loc.LocationSqid, userInformation, true);
        }

        /// <summary>
        /// Checks if the organization is valid for the user.
        /// </summary>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="userInformation">The user information.</param>
        /// <returns>True if the organization is valid for the user, otherwise false.</returns>
        private async Task<bool> OrganizationIsValidForUser(string organizationSqid, UserInformation userInformation)
        {
            int? orgCurrentStatus = null;
            using (var _context = _repositoryFactory.Create())
            {
                var org = await _context.Organizations.FirstOrDefaultAsync(x => x.OrganizationSqid == organizationSqid);
                if (org != null)
                {
                    orgCurrentStatus = org.CurrentStatus;
                }
            }
            if (orgCurrentStatus == null)
            {
                return false;
            }
            if (orgCurrentStatus != 0 && !(userInformation.IsSuperAdmin || (await _authorizationService.IsOrgAdminAsync(userInformation.UserInformationSqid, organizationSqid))))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Sets the current occupancy value for a location.
        /// </summary>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="locationSqid">The location SQID.</param>
        /// <param name="currentOccupancy">The current occupancy value.</param>
        /// <param name="userInformation">The user information.</param>
        /// <returns>The updated current occupancy value.</returns>
        /// <exception cref="Exception">Thrown when the organization is not found or the user does not have rights to update the location.</exception>
        public async Task<int> SetLocationCurrentOccupancy(string organizationSqid, string locationSqid, int currentOccupancy, UserInformation userInformation)
        {
            if (!(await OrganizationIsValidForUser(organizationSqid, userInformation)))
            {
                throw new Exception("Organization not found");
            }
            string userInformationSqid = userInformation.UserInformationSqid;
            if (!(await _authorizationService.IsLocAdminAsync(userInformationSqid, organizationSqid, locationSqid)))
            {
                await _authorizationService.LogAccessExceptionAsync(userInformationSqid, organizationSqid, locationSqid, "", "", $"User does not have rights to update location {locationSqid}", $"User does not have rights to update location {locationSqid}");
            }
            var loc = await GetAsync(organizationSqid, locationSqid, userInformation, true);
            if (loc == null)
            {
                throw new KeyNotFoundException($"Location {locationSqid} was not found");
            }
            if (loc.CurrentStatus == 2 && !userInformation.IsSuperAdmin)
            {
                throw new KeyNotFoundException($"Location {locationSqid}  was not found");
            }
            loc.CurrentOccupancy = currentOccupancy;
            await InternalSaveAsync(loc, organizationSqid, userInformation);
            return loc.CurrentOccupancy;
        }

        /// <summary>
        /// Gets the occupancy stats for a location.
        /// </summary>
        /// <param name="locationSqid">The location SQID.</param>
        /// <returns>The occupancy stats for the location.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the location is not found.</exception>
        public async Task<LocationOccupancyStats> GetLocationOccupancyStats(string locationSqid)
        {
            LocationOccupancyStats retVal = new LocationOccupancyStats();
            using (var context = _repositoryFactory.Create())
            {
                var loc = await context.Locations.FirstOrDefaultAsync(e => e.LocationSqid == locationSqid);
                if (loc != null)
                {
                    retVal.MaxOccupancy = loc.MaxOccupancy;
                    retVal.CurrentOccupancy = loc.CurrentOccupancy;
                    retVal.OccupancyThresholdWarning = loc.OccupancyThresholdWarning;
                }
                else
                {
                    throw new InvalidOperationException("Location not found for sqid " + locationSqid);
                }
            }
            return retVal;
        }
    }
}
