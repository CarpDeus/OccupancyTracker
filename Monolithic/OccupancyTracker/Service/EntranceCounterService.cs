using Enyim.Caching;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using OccupancyTracker.DTO;
using OccupancyTracker.IService;
using OccupancyTracker.Models;
using Sqids;

namespace OccupancyTracker.Service
{
    public class EntranceCounterService : IEntranceCounterService
    {
        private readonly IDbContextFactory<OccupancyContext> _contextFactory;
        private readonly ISqidsEncoderFactory _sqids;
        private readonly IMemcachedClient _memcachedClient;
        private readonly IOccAuthorizationService _authorizationService;

        public EntranceCounterService(IDbContextFactory<OccupancyContext> contextFactory, ISqidsEncoderFactory sqidsEncoderFactory, IMemcachedClient memcachedClient,
            IOccAuthorizationService authorizationService)
        {
            _authorizationService = authorizationService;
            _memcachedClient = memcachedClient;
            _contextFactory = contextFactory;
            _sqids = sqidsEncoderFactory;
        }

        public async Task<EntranceCounter> GetAsync(string sqid, bool forceCacheRefresh = false)
        {
            ParsedOrganizationSqids pos = _sqids.DecodeSqids(null, null, null, sqid);
            using var _context = _contextFactory.CreateDbContext();
            return await _context.EntranceCounters
                .Where(e => e.EntranceCounterSqid == sqid && e.CurrentStatus == Statuses.DataStatus.Active.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<EntranceCounter> ReplaceAsync(Entrance entrance)
        {
            throw new NotImplementedException();
        }

        public async Task<int> UpdateCountAsync(string sqid, int count, bool forceCacheRefresh = false)
        {
            ParsedOrganizationSqids pos = _sqids.DecodeSqids(null, null, null, sqid);
            using var _context = _contextFactory.CreateDbContext();
            var location = await _context.Locations.FirstOrDefaultAsync(l => l.LocationId == (long)pos.LocationId);
            if (location == null) return 0;

            location.CurrentOccupancy += count;
            var occupancyLog = new OccupancyLog
            {
                OrganizationId = (long)pos.OrganizationId,
                LocationId = (long)pos.LocationId,
                EntranceId = (long)pos.EntranceId,
                EntranceCounterId = (long)pos.EntranceCounterId,
                LoggedChange = count,
                CurrentOccupancy = location.CurrentOccupancy,
                CreatedDate = DateTime.Now
            };
            _context.OccupancyLogs.Add(occupancyLog);
            await _context.SaveChangesAsync();

            return location.CurrentOccupancy;
        }

        public async Task<int> GetCountAsync(string sqid, bool forceCacheRefresh = false)
        {
            ParsedOrganizationSqids pos = _sqids.DecodeSqids(null, null, sqid);
            using var _context = _contextFactory.CreateDbContext();
            var location = await _context.Locations.FirstOrDefaultAsync(l => l.LocationId == (long)pos.LocationId);
            return location?.CurrentOccupancy ?? 0;
        }

        public async Task<string> GetLocationSqidAsync(string sqid)
        {
            return _sqids.DecodeSqids(null, null, null, sqid).LocationSqid;
        }

        public async Task<string> CreateEntranceCounterForEntranceAsync(string userInformationSqid, Entrance entrance)
        {
            var eci = new EntranceCounter
            {
                EntranceId = entrance.EntranceId,
                LocationId = entrance.LocationId,
                OrganizationId = entrance.OrganizationId,
                CurrentStatus = 0,
                CreatedBy = userInformationSqid,
                CreatedDate = entrance.CreatedDate
            };
            using var _context = _contextFactory.CreateDbContext();
            _context.EntranceCounters.Add(eci);
            await _context.SaveChangesAsync();

            eci.EntranceCounterSqid = _sqids.EncodeEntranceCounterId(eci.OrganizationId, eci.LocationId, eci.EntranceId, eci.EntranceCounterId);
            _context.Update(eci);
            entrance.EntranceCounterInstanceSqid = eci.EntranceCounterSqid;
            _context.Entrances.Update(entrance);
            await _context.SaveChangesAsync();

            return eci.EntranceCounterSqid;
        }

        public async Task<EntranceCounterDto?> GetCounterForTrackerAsync(string sqid, bool forceCacheRefresh = false)
        {
            var entranceCounter = await GetAsync(sqid, forceCacheRefresh);
            if (entranceCounter == null) return new EntranceCounterDto();

            var retVal = new EntranceCounterDto(entranceCounter);
            if (retVal.CurrentStatus != Statuses.DataStatus.Active.Id) return null;

            using var _context = _contextFactory.CreateDbContext();
            var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.OrganizationId == retVal.OrganizationId);
            if (organization?.CurrentStatus != Statuses.DataStatus.Active.Id) return null;

            retVal.OrganizationName = organization.OrganizationName;
            retVal.OrganizationSqid = organization.OrganizationSqid;

            var location = await _context.Locations.FirstOrDefaultAsync(l => l.LocationId == retVal.LocationId);
            if (location?.CurrentStatus != Statuses.DataStatus.Active.Id) return null;

            retVal.LocationName = location.LocationName;
            retVal.StartingCounter = location.CurrentOccupancy;
            retVal.CurrentOccupancy = location.CurrentOccupancy;
            retVal.MaxOccupancy = location.MaxOccupancy;
            retVal.OccupancyThresholdWarning = location.OccupancyThresholdWarning;
            retVal.LocationSqid = location.LocationSqid;

            var entrance = await _context.Entrances.FirstOrDefaultAsync(e => e.EntranceId == retVal.EntranceId);
            if (entrance?.CurrentStatus != Statuses.DataStatus.Active.Id || entrance.EntranceCounterInstanceSqid != sqid) return null;

            retVal.EntranceName = entrance.EntranceName;
            retVal.EntranceSqid = entrance.EntranceSqid;

            return retVal;
        }
    }
}
