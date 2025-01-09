using Enyim.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using OccupancyTracker.IService;
using OccupancyTracker.Models;
//using OccupancyTracker.Models.Migrations;
using Sqids;
using System.Collections.Generic;
using System.Text;

namespace OccupancyTracker.Service
{
    public class LocationService : ILocationService
    {
        private readonly IDbContextFactory<OccupancyContext> _contextFactory;
        private readonly ISqidsEncoderFactory _sqids;
        private readonly IMemcachedClient _memcachedClient;
        private readonly IOccAuthorizationService _authorizationService;

        public LocationService(IDbContextFactory<OccupancyContext> contextFactory, ISqidsEncoderFactory sqidsEncoderFactory, IMemcachedClient memcachedClient,
            IOccAuthorizationService authorizationService)
        {
            _authorizationService = authorizationService;
            _memcachedClient = memcachedClient;
            _contextFactory = contextFactory;
            _sqids = sqidsEncoderFactory;
        }

        public async Task<Location?> ChangeStatusAsync(string organizationSqid, string locationSqid, int fromStatus, int toStatus, UserInformation userInformation)
        {
            if(!(await OrganizationIsValidForUser(organizationSqid, userInformation)))
            {
                throw new Exception("Organization not found");
            }
            string userInformationSqid = userInformation.UserInformationSqid;
            if (!(await _authorizationService.IsLocAdminAsync(userInformationSqid, organizationSqid, locationSqid)))
            {
                _authorizationService.LogAccessExceptionAsync(userInformationSqid, organizationSqid, locationSqid, "", "", $"User does not have rights to update location {locationSqid}", $"User does not have rights to update location {locationSqid}");
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
            using (var _context = _contextFactory.CreateDbContext())
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

        public async Task<List<Location>> GetActiveListAsync(UserInformation userInformation, string organizationSqid, string filter, bool forceCacheRefresh = false)
        {
            if (userInformation.IsSuperAdmin)
            {
                return (await GetFullListAsync(userInformation, organizationSqid, filter, forceCacheRefresh)).Where(e => e.CurrentStatus == 0).ToList();
            }
            else return (await GetListAsync(userInformation, organizationSqid, filter, forceCacheRefresh)).Where(e => e.CurrentStatus == 0).ToList();
        }

        public async Task<Location?> GetAsync(string organizationSqid, string locationSqid, UserInformation userInformation, bool forceCacheRefresh = false)
        {
            if (!(await OrganizationIsValidForUser(organizationSqid, userInformation)))
            {
                throw new Exception("Organization not found");
            }
            string cacheKey = $"Loc:{locationSqid}";
            Location? loc = null;// _memcachedClient.Get<Location>(cacheKey);
            if (loc == null || forceCacheRefresh)
            {
                using (var context = _contextFactory.CreateDbContext())
                {
                    loc = context.Locations.FirstOrDefault(e => e.LocationSqid == locationSqid);
                    if (loc != null)
                        await _memcachedClient.SetAsync(cacheKey, loc, 15);
                    else
                    {
                        throw new InvalidOperationException("Location not found for sqid " + locationSqid);
                    }
                }
            }
            return loc;
        }

        public async Task<List<Location>> GetDeletedListAsync(UserInformation userInformation, string organizationSqid, string filter, bool forceCacheRefresh = false)
        {
            if (userInformation.IsSuperAdmin)
            {
                return (await GetFullListAsync(userInformation, organizationSqid, filter, forceCacheRefresh)).Where(e => e.CurrentStatus == 1).ToList();
            }
            else return (await GetListAsync(userInformation, organizationSqid, filter, forceCacheRefresh)).Where(e => e.CurrentStatus == 1).ToList() ;
        }

        public async Task<List<Location>> GetFullListAsync(UserInformation userInformation, string organizationSqid, string filter = "", bool forceCacheRefresh = false)
        {

            if (!userInformation.IsSuperAdmin) return new List<Location>();
            string cacheKey = $"LocList:{organizationSqid}:{userInformation.UserInformationSqid}";
            List<Location> retVal = new();// _memcachedClient.Get<List<Location>>(cacheKey);
            ParsedOrganizationSqids pos = _sqids.DecodeSqids(organizationSqid);
            //if ( retVal == null || retVal.Count==0 || forceCacheRefresh)
            {
                retVal = _contextFactory.CreateDbContext().Locations
                .Where(e => e.OrganizationId == (long)pos.OrganizationId).ToList();
                if (retVal != null)
                    await _memcachedClient.SetAsync(cacheKey, retVal, 15);
                else
                {
                    throw new InvalidOperationException("Location List not found for OganizationSqid " + organizationSqid);
                }
            }
            return retVal.Where(x => x.FilterCriteria(filter)).ToList();
        }



        public async Task<List<Location>> GetListAsync(UserInformation userInformation, string organizationSqid, string filter = "", bool forceCacheRefresh = false)
        {
            if(!(await OrganizationIsValidForUser(organizationSqid, userInformation)))
            {
                throw new Exception("Organization not found");
            }
            string cacheKey = $"LocList:{organizationSqid}:{userInformation.UserInformationSqid}";
            List<Location> retVal = new();// _memcachedClient.Get<List<Location>>(cacheKey);
            ParsedOrganizationSqids pos = _sqids.DecodeSqids(organizationSqid);
            //if (retVal == null || retVal.Count==0|| forceCacheRefresh)
            {
                bool isOrgAdmin = await _authorizationService.IsOrgAdminAsync(userInformation.UserInformationSqid, organizationSqid);
                retVal = _contextFactory.CreateDbContext().Locations
                .Where(e => e.OrganizationId == (long)pos.OrganizationId)
                .Where(e => e.CurrentStatus ==0 || (isOrgAdmin && e.CurrentStatus==1))
                .ToList();
                if (retVal != null)
                    await _memcachedClient.SetAsync(cacheKey, retVal, 15);
                else
                {
                    throw new InvalidOperationException("Location List not found for OganizationSqid " + organizationSqid);
                }
            }
            return retVal.Where(x => x.FilterCriteria(filter)).ToList();
        }

        public async Task<List<Location>> GetPermanentlyDeletedListAsync(UserInformation userInformation, string organizationSqid, string filter, bool forceCacheRefresh = false)
        {
            return (await GetFullListAsync(userInformation, organizationSqid, filter, forceCacheRefresh)).Where(x => x.CurrentStatus == 2).ToList();
        }

        public async Task<Location> SaveAsync(Location location, string organizationSqid, UserInformation userInformation)
        {
            return await InternalSaveAsync(location,organizationSqid, userInformation);
        }


        /// <summary>
        /// InternalSaveAsync organization
        /// </summary>
        /// <param name="loc"></param>
        /// <returns></returns>
        private async Task<Location> InternalSaveAsync(Location loc,string organizationSqid,  UserInformation userInformation)
        {
            if(!(await OrganizationIsValidForUser(organizationSqid, userInformation)))
            {
                throw new Exception("Organization not found");
            }
            using (var _context = _contextFactory.CreateDbContext())
            {
                _context.ChangeTracker.Clear();
                if (loc.LocationId == 0)
                {
                    await _context.Locations.AddAsync(loc);
                    await _context.SaveChangesAsync();
                    loc.LocationSqid = _sqids.EncodeLocationId(loc.OrganizationId, loc.LocationId);
                    //_context.Locations.Update(loc);
                    //_context.Entry(loc).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
                else
                {
                    if (string.IsNullOrEmpty(loc.LocationSqid))
                    {
                        loc.LocationSqid = _sqids.EncodeLocationId(loc.OrganizationId, loc.LocationId);
                    }
                    _context.Locations.Update(loc);
                    _context.Entry(loc).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
                await _context.SaveChangesAsync();
            }

            return await GetAsync(organizationSqid, loc.LocationSqid, userInformation, true);
        }


        private async Task<bool> OrganizationIsValidForUser(string organizationSqid, UserInformation userInformation)
        {
            int? orgCurrentStatus = null;
            using (var _context = _contextFactory.CreateDbContext())
            {
                var org = _context.Organizations.FirstOrDefault(x => x.OrganizationSqid == organizationSqid);
                if (org != null)
                {
                    orgCurrentStatus = org.CurrentStatus;
                }                
            }
            if (orgCurrentStatus == null)
            {
                return false;
            }
            if(orgCurrentStatus != 0 && !(userInformation.IsSuperAdmin || (await _authorizationService.IsOrgAdminAsync(userInformation.UserInformationSqid, organizationSqid))))
            {
                return false;
            }
            return true;
        }
    }
}
