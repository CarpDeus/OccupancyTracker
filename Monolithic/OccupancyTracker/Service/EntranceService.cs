using Enyim.Caching;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.IService;
using OccupancyTracker.Models;
using SendGrid.Helpers.Mail;
using Sqids;
using System.Runtime.CompilerServices;

namespace OccupancyTracker.Service
{
    /// <summary>
    /// Service for managing entrances.
    /// </summary>
    public class EntranceService : IEntranceService
    {
        private readonly IDbContextFactory<OccupancyContext> _contextFactory;
        private readonly ISqidsEncoderFactory _sqids;
        private readonly IMemcachedClient _memcachedClient;
        private readonly IOccAuthorizationService _authorizationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="EntranceService"/> class.
        /// </summary>
        /// <param name="contextFactory">The context factory.</param>
        /// <param name="sqidsEncoderFactory">The SQIDs encoder factory.</param>
        /// <param name="memcachedClient">The Memcached client.</param>
        /// <param name="authorizationService">The authorization service.</param>
        public EntranceService(IDbContextFactory<OccupancyContext> contextFactory, ISqidsEncoderFactory sqidsEncoderFactory, IMemcachedClient memcachedClient,
            IOccAuthorizationService authorizationService)
        {
            _authorizationService = authorizationService;
            _memcachedClient = memcachedClient;
            _contextFactory = contextFactory;
            _sqids = sqidsEncoderFactory;
        }

        /// <summary>
        /// Changes the status of an entrance.
        /// </summary>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="locationSqid">The location SQID.</param>
        /// <param name="entranceSqid">The entrance SQID.</param>
        /// <param name="fromStatus">The current status.</param>
        /// <param name="toStatus">The new status.</param>
        /// <param name="userInformation">The user making the call.</param>
        /// <returns>The updated entrance.</returns>
        public async Task<Entrance?> ChangeStatusAsync(string organizationSqid, string locationSqid, string entranceSqid, int fromStatus, int toStatus, UserInformation userInformation)
        {
            string userInformationSqid = userInformation.UserInformationSqid;
            var entity = await GetAsync(organizationSqid, locationSqid, entranceSqid, userInformation, true);
            if (entity == null)
            {
                throw new InvalidOperationException($"Entrance does not exist for {entranceSqid}.");
            }
            if (!await _authorizationService.IsLocAdminAsync(userInformationSqid, organizationSqid, locationSqid))
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

        /// <summary>
        /// Gets a list of entrances without any filtering. Only super admins can access this method.
        /// </summary>
        /// <param name="userInformation">The user making the call.</param>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="locationSqid">The location SQID.</param>
        /// <param name="forceCacheRefresh">When true force getting from data store instead of cache</param>
        /// <returns>List of entrances</returns>
        private async Task<List<Entrance>> InternalGetUnfilteredListAll(UserInformation userInformation, string organizationSqid, string locationSqid, bool forceCacheRefresh = false)
        {
            if (!userInformation.IsSuperAdmin) return new List<Entrance>();
            string userInformationSqid = userInformation.UserInformationSqid;
            string cacheKey = $"EntList:{locationSqid}:{userInformationSqid}:Ents";
            List<Entrance> entrances = new List<Entrance>();
            if (string.IsNullOrEmpty(locationSqid)) return entrances;
            using (var _context = _contextFactory.CreateDbContext())
            {
                ParsedOrganizationSqids pos = _sqids.DecodeSqids(null, locationSqid);
                entrances = _context.Entrances.Where(x => x.LocationId == pos.LocationId).ToList();
            }
            await _memcachedClient.SetAsync(cacheKey, entrances, 300);
            return entrances;
        }

        /// <summary>
        /// Gets a list of entrances user has access to without any filtering.
        /// </summary>
        /// <param name="userInformation">The user making the call.</param>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="locationSqid">The location SQID.</param>
        /// <param name="forceCacheRefresh">When true force getting from data store instead of cache</param>
        /// <returns>List of entrances</returns>
        private async Task<List<Entrance>> InternalGetUnfilteredList(UserInformation userInformation, string organizationSqid, string locationSqid, bool forceCacheRefresh = false)
        {
            string userInformationSqid = userInformation.UserInformationSqid;
            string cacheKey = $"EntList:{locationSqid}:{userInformationSqid}:Ents";
            List<Entrance> entrances = new List<Entrance>();
            if (string.IsNullOrEmpty(locationSqid)) return entrances;
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
            return entrances;
        }

        /// <summary>
        /// Gets a list of active entrances.
        /// </summary>
        /// <param name="userInformation">The user making the call.</param>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="locationSqid">The location SQID.</param>
        /// <param name="filter">The filter criteria.</param>
        /// <param name="forceCacheRefresh">If set to <c>true</c>, forces cache refresh.</param>
        /// <returns>A list of active entrances.</returns>
        public async Task<List<Entrance>> GetActiveListAsync(UserInformation userInformation, string organizationSqid, string locationSqid, string filter, bool forceCacheRefresh = false)
        {
            return (await GetListAsync(userInformation, organizationSqid, locationSqid, filter, forceCacheRefresh)).Where(x => x.CurrentStatus == Statuses.DataStatus.Active.Id).ToList();
        }

        /// <summary>
        /// Gets an entrance by its SQID.
        /// </summary>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="locationSqid">The location SQID.</param>
        /// <param name="entranceSqid">The entrance SQID.</param>
        /// <param name="userInformation">The user making the call.</param>
        /// <param name="forceCacheRefresh">If set to <c>true</c>, forces cache refresh.</param>
        /// <returns>The entrance.</returns>
        public async Task<Entrance?> GetAsync(string organizationSqid, string locationSqid, string entranceSqid, UserInformation userInformation, bool forceCacheRefresh = false)
        {
            string userInformationSqid = userInformation.UserInformationSqid;
            if (!await _authorizationService.HasAccessToLocationAsync(userInformationSqid, locationSqid))
            {
                _authorizationService.LogAccessExceptionAsync(userInformationSqid, "", entranceSqid, locationSqid, "", $"User does not have access to the entrances for Location {locationSqid}", $"You do not have access to the entrances for Location {locationSqid}");
            }

            string cacheKey = $"Entrance:{entranceSqid}";
            Entrance entrance = new();
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

        /// <summary>
        /// Gets a list of deleted entrances.
        /// </summary>
        /// <param name="userInformation">The user making the call.</param>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="locationSqid">The location SQID.</param>
        /// <param name="filter">The filter criteria.</param>
        /// <param name="forceCacheRefresh">If set to <c>true</c>, forces cache refresh.</param>
        /// <returns>A list of deleted entrances.</returns>
        public async Task<List<Entrance>> GetDeletedListAsync(UserInformation userInformation, string organizationSqid, string locationSqid, string filter, bool forceCacheRefresh = false)
        {
            return (await GetListAsync(userInformation, organizationSqid, locationSqid, filter, forceCacheRefresh)).Where(x => x.CurrentStatus == Statuses.DataStatus.Deleted.Id).ToList();
        }

        /// <summary>
        /// Gets a list of entrances.
        /// </summary>
        /// <param name="userInformation">The user making the call.</param>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="locationSqid">The location SQID.</param>
        /// <param name="filter">The filter criteria.</param>
        /// <param name="forceCacheRefresh">If set to <c>true</c>, forces cache refresh.</param>
        /// <returns>A list of entrances.</returns>
        public async Task<List<Entrance>> GetListAsync(UserInformation userInformation, string organizationSqid, string locationSqid, string filter = "", bool forceCacheRefresh = false)
        {
            if (userInformation.IsSuperAdmin) return (await InternalGetUnfilteredListAll(userInformation, organizationSqid, locationSqid, forceCacheRefresh)).Where(x => x.FilterCriteria(filter)).ToList();
            else
                return (await InternalGetUnfilteredList(userInformation, organizationSqid, locationSqid, forceCacheRefresh)).Where(x => x.FilterCriteria(filter)).ToList();
        }

        /// <summary>
        /// Gets a list of permanently deleted entrances.
        /// </summary>
        /// <param name="userInformation">The user making the call.</param>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="locationSqid">The location SQID.</param>
        /// <param name="filter">The filter criteria.</param>
        /// <param name="forceCacheRefresh">If set to <c>true</c>, forces cache refresh.</param>
        /// <returns>A list of permanently deleted entrances.</returns>
        public async Task<List<Entrance>> GetPermanentlyDeletedListAsync(UserInformation userInformation, string organizationSqid, string locationSqid, string filter, bool forceCacheRefresh = false)
        {
            return (await GetListAsync(userInformation, organizationSqid, locationSqid, filter, forceCacheRefresh)).Where(x => x.CurrentStatus == Statuses.DataStatus.PermanentlyDeleted.Id).ToList();
        }

        /// <summary>
        /// Saves an entrance.
        /// </summary>
        /// <param name="entrance">The entrance.</param>
        /// <param name="organizationSqid">The organization SQID.</param>
        /// <param name="locationSqid">The location SQID.</param>
        /// <param name="userInformation">The user making the call.</param>
        /// <returns>The saved entrance.</returns>
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


        /// <summary>
        /// Saves the entrance to the datastore.
        /// </summary>
        /// <param name="userInformation">User making the call</param>
        /// <param name="entrance">Updated entrance</param>
        /// <returns>Entrance</returns>
        /// <exception cref="InvalidOperationException">If the user doesn't have permission to update the data</exception>
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
            await GetListAsync(userInformation, pos.OrganizationSqid, pos.LocationSqid, "", true);
            return await GetAsync(pos.OrganizationSqid, pos.LocationSqid, entrance.EntranceSqid, userInformation, true);
        }
    }
}
