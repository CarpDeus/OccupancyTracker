using Enyim.Caching;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.IService;
using OccupancyTracker.Models;
using SendGrid.Helpers.Mail;
using Sqids;
using System.Runtime.CompilerServices;

namespace OccupancyTracker.Service
{
    public class EntranceService : IEntranceService
    {

        private readonly IDbContextFactory<OccupancyContext> _contextFactory;
        private readonly ISqidsEncoderFactory _sqids;
        private readonly IMemcachedClient _memcachedClient;
        private readonly IOccAuthorizationService _authorizationService;
        
        public EntranceService(IDbContextFactory<OccupancyContext> contextFactory, ISqidsEncoderFactory sqidsEncoderFactory, IMemcachedClient memcachedClient,
            IOccAuthorizationService authorizationService)
        {
            _authorizationService = authorizationService;
            _memcachedClient = memcachedClient;
            _contextFactory = contextFactory;
            _sqids = sqidsEncoderFactory;
        }

        public async Task<Entrance?> ChangeStatusAsync(string organizationSqid, string locationSqid, string entranceSqid, int fromStatus, int toStatus, UserInformation userInformation)
        {
            string userInformationSqid = userInformation.UserInformationSqid;
            var entity = await GetAsync(organizationSqid, locationSqid, entranceSqid, userInformation, true);
            if (entity == null)
            {
                throw new InvalidOperationException($"Entrance does not exist for {entranceSqid}.");
            }
            if (!await _authorizationService.IsLocAdminAsync(userInformationSqid, organizationSqid,locationSqid))
            {
                _authorizationService.LogAccessExceptionAsync(userInformationSqid, organizationSqid, locationSqid, entranceSqid, "", $"User does not have access to the location containing entrance {entranceSqid}", $"You do not have access to the location containing entrance {entranceSqid}");
            }
            using (var _context = _contextFactory.CreateDbContext())
            {
                if (entity != null)
                {
                    if (entity.CurrentStatus == fromStatus)
                    {
                        entity.CurrentStatus = toStatus;
                        await SaveAsync(entity, organizationSqid, locationSqid, userInformation);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Entity is not in the correct CurrentStatus to perform this operation. CurrentStatus must be {Statuses.DataStatus.FromId(fromStatus).Name} but is currently {Statuses.DataStatus.FromId(entity.CurrentStatus).Name}.");
                    }
                }
            }

            await GetListAsync(userInformation, organizationSqid, locationSqid, "", true);

            return await GetAsync(organizationSqid, locationSqid, entity.EntranceSqid, userInformation, true);

        }

        private async Task<List<Entrance>> InternalGetUnfilteredListAll(UserInformation userInformation, string organizationSqid, string locationSqid, bool forceCacheRefresh = false)
        {
            if(!userInformation.IsSuperAdmin) return new List<Entrance>();
            string userInformationSqid = userInformation.UserInformationSqid;
            //  $"LocList:{organizationSqid}:{userInformationSqid}";
            string cacheKey = $"EntList:{locationSqid}:{userInformationSqid}:Ents";
            List<Entrance> entrances = new List<Entrance>();
            if (string.IsNullOrEmpty(locationSqid)) return entrances;
            //entrances = _memcachedClient.Get<List<Entrance>>(cacheKey);
            //if (entrances == null || forceCacheRefresh)
            {
                using (var _context = _contextFactory.CreateDbContext())
                {
                    ParsedOrganizationSqids pos = _sqids.DecodeSqids(null, locationSqid);
                    entrances = _context.Entrances.Where(x => x.LocationId == pos.LocationId).ToList();
                }
                await _memcachedClient.SetAsync(cacheKey, entrances, 300);
            }
            return entrances;
        }

        private async Task<List<Entrance>> InternalGetUnfilteredList(UserInformation userInformation, string organizationSqid, string locationSqid, bool forceCacheRefresh = false)
        {
            string userInformationSqid = userInformation.UserInformationSqid;
            //  $"LocList:{organizationSqid}:{userInformationSqid}";
            string cacheKey = $"EntList:{locationSqid}:{userInformationSqid}:Ents";
            List<Entrance> entrances = new List<Entrance>();
            if (string.IsNullOrEmpty(locationSqid)) return entrances;
            //entrances = _memcachedClient.Get<List<Entrance>>(cacheKey);
        //    if (entrances == null || forceCacheRefresh)
            {
                using (var _context = _contextFactory.CreateDbContext())
                {
                    if (!await _authorizationService.HasAccessToLocationAsync(userInformationSqid, locationSqid))
                    {
                        _authorizationService.LogAccessExceptionAsync(userInformationSqid, "", locationSqid, "", "", $"User does not have access to the entrances for Location {locationSqid}", $"You do not have access to the entrances for Location {locationSqid}");
                    }
                    ParsedOrganizationSqids pos = _sqids.DecodeSqids(null, locationSqid);
                    bool isLocAdmin = await _authorizationService.IsLocAdminAsync(userInformationSqid, pos.OrganizationSqid, pos.LocationSqid);
                    entrances = _context.Entrances.Where(x => x.LocationId == pos.LocationId)
                        .Where(x => x.CurrentStatus != Statuses.DataStatus.PermanentlyDeleted.Id)
                        .ToList();
                }
                await _memcachedClient.SetAsync(cacheKey, entrances, 30);
            }
            return entrances;
        }


        public async Task<List<Entrance>> GetActiveListAsync(UserInformation userInformation, string organizationSqid, string locationSqid, string filter, bool forceCacheRefresh = false)
        {
            return (await GetListAsync(userInformation, organizationSqid, locationSqid, filter, forceCacheRefresh)).Where(x => x.CurrentStatus == Statuses.DataStatus.Active.Id).ToList();
        }

        public async Task<Entrance?> GetAsync(string organizationSqid, string locationSqid, string entranceSqid, UserInformation userInformation, bool forceCacheRefresh = false)
        {
            string userInformationSqid = userInformation.UserInformationSqid;
            if (!await _authorizationService.HasAccessToLocationAsync(userInformationSqid, locationSqid))
            {
                _authorizationService.LogAccessExceptionAsync(userInformationSqid, "", entranceSqid, locationSqid, "", $"User does not have access to the entrances for Location {locationSqid}", $"You do not have access to the entrances for Location {locationSqid}");
            }

            string cacheKey = $"Entrance:{entranceSqid}";
            Entrance entrance = new();
            //if (!forceCacheRefresh)
            //{
            //    entrance = _memcachedClient.Get<Entrance>(cacheKey);
            //}
            //if (entrance != null && !forceCacheRefresh)
            //{
            //    return entrance;
            //}
            using (var _context = _contextFactory.CreateDbContext())
            {
                entrance = _context.Entrances.FirstOrDefault(e => e.EntranceSqid == entranceSqid);
                if (entrance != null)
                {
                    _memcachedClient.Set(cacheKey, entrance, 30);
                }
                return entrance;
            }

        }

        public async Task<List<Entrance>> GetDeletedListAsync(UserInformation userInformation, string organizationSqid, string locationSqid, string filter, bool forceCacheRefresh = false)
        {
            return (await GetListAsync(userInformation,organizationSqid,locationSqid,filter,forceCacheRefresh)).Where(x => x.CurrentStatus == Statuses.DataStatus.Deleted.Id).ToList();
        }


        public async Task<List<Entrance>> GetListAsync(UserInformation userInformation, string organizationSqid, string locationSqid, string filter = "", bool forceCacheRefresh = false)
        {
            if (userInformation.IsSuperAdmin) return (await InternalGetUnfilteredListAll(userInformation, organizationSqid, locationSqid, forceCacheRefresh)).Where(x => x.FilterCriteria(filter)).ToList();
            else
                return (await InternalGetUnfilteredList(userInformation, organizationSqid, locationSqid, forceCacheRefresh)).Where(x => x.FilterCriteria(filter)).ToList();
        }
        

        public async Task<List<Entrance>> GetPermanentlyDeletedListAsync(UserInformation userInformation, string organizationSqid, string locationSqid, string filter, bool forceCacheRefresh = false)
        {
            return (await GetListAsync(userInformation, organizationSqid, locationSqid, filter, forceCacheRefresh)).Where(x => x.CurrentStatus == Statuses.DataStatus.PermanentlyDeleted.Id).ToList();
        }

        public async Task<Entrance> SaveAsync(Entrance entrance, string organizationSqid, string locationSqid, UserInformation userInformation)
        {
            string userInformationSqid = userInformation.UserInformationSqid;
            if (!await _authorizationService.IsLocAdminAsync(userInformationSqid, organizationSqid, locationSqid))
            {
                _authorizationService.LogAccessExceptionAsync(userInformationSqid, organizationSqid, locationSqid, entrance.EntranceSqid, "", $"User does not have access to the location containing entrance {entrance.EntranceSqid}", $"You do not have access to the location containing entrance {entrance.EntranceSqid}");
            }
            if (entrance.LocationId == 0)
            {
                throw new InvalidOperationException("Location must be set before saving.");
            }
            if (entrance.EntranceId == 0)
            {
                return entrance = await SaveToDatastoreAsync(userInformation, entrance);
            }
            else
                return await SaveToDatastoreAsync(userInformation, entrance);

        }


        private async Task<Entrance> SaveToDatastoreAsync(UserInformation userInformation, Entrance entrance)
        {
            string userInformationSqid = userInformation.UserInformationSqid;

            using (var _context = _contextFactory.CreateDbContext())
            {
                if (entrance.EntranceId == 0)
                {
                    _context.Entrances.Add(entrance);
                    try
                    {
                        await _context.SaveChangesAsync();
                        entrance.EntranceSqid = _sqids.EncodeEntranceId(entrance.OrganizationId, entrance.LocationId, entrance.EntranceId);
                        _context.Entrances.Update(entrance);
                        _context.Entry(entrance).State = EntityState.Modified;
                        await _context.SaveChangesAsync();
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException("Error saving entrance to database", e);
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(entrance.EntranceSqid))
                    {
                        entrance.EntranceSqid = _sqids.EncodeEntranceId(entrance.OrganizationId, entrance.LocationId, entrance.EntranceId);
                    }
                    _context.Entrances.Update(entrance);
                    _context.Entry(entrance).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
                await _context.SaveChangesAsync();
            }
            ParsedOrganizationSqids pos = _sqids.DecodeSqids(null, null, entrance.EntranceSqid);
            await GetListAsync(userInformation,pos.OrganizationSqid, pos.LocationSqid, "",true);
            return await GetAsync(pos.OrganizationSqid,pos.LocationSqid, entrance.EntranceSqid, userInformation, true);
        }


    }
}
