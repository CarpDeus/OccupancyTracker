using System.Data;
using OccupancyTracker.IRepository;
using OccupancyTracker.DTO;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service;

public enum TrackerAccess { Success, NotFound, Unauthenticated, Forbidden }
public record TrackerResult(TrackerAccess Access, EntranceCounterDto? Counter = null);

public sealed class TrackerOperations(
    IRepositorySessionFactory contextFactory,
    RequestAuthorizationService authorization,
    IHttpContextAccessor httpContextAccessor)
{
    public Task<TrackerResult> ReadAsync(string counterSqid) => ExecuteAsync(counterSqid, null);
    public Task<TrackerResult> UpdateAsync(string counterSqid, int delta) => ExecuteAsync(counterSqid, delta);

    private async Task<TrackerResult> ExecuteAsync(string counterSqid, int? delta)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ExecuteOnceAsync(counterSqid, delta);
            }
            catch (Exception ex) when (attempt < maxAttempts && contextFactory.IsConcurrencyConflict(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt));
            }
        }
    }

    private async Task<TrackerResult> ExecuteOnceAsync(string counterSqid, int? delta)
    {
        await using var context = await contextFactory.CreateAsync();
        await using var transaction = await context.BeginTransactionAsync(IsolationLevel.Serializable);
        var data = await RequestAuthorizationService.LoadCurrentCounterAsync(context, counterSqid);
        if (data == null)
            return new TrackerResult(TrackerAccess.NotFound);

        if (string.IsNullOrEmpty(counterSqid) || !await authorization.CanUseCounterAsync(context, data))
            return new TrackerResult(httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true
                ? TrackerAccess.Forbidden : TrackerAccess.Unauthenticated);

        if (delta.HasValue)
        {
            data.Location.CurrentOccupancy = checked(data.Location.CurrentOccupancy + delta.Value);
            context.OccupancyLogs.Add(new OccupancyLog
            {
                OrganizationId = data.Organization.OrganizationId,
                LocationId = data.Location.LocationId,
                EntranceId = data.Entrance.EntranceId,
                EntranceCounterId = data.Counter.EntranceCounterId,
                LoggedChange = delta.Value,
                CurrentOccupancy = data.Location.CurrentOccupancy,
                CreatedDate = DateTime.Now
            });
            await context.SaveChangesAsync();
        }

        var result = new EntranceCounterDto(data.Counter)
        {
            OrganizationName = data.Organization.OrganizationName,
            OrganizationSqid = data.Organization.OrganizationSqid,
            LocationName = data.Location.LocationName,
            LocationSqid = data.Location.LocationSqid,
            EntranceName = data.Entrance.EntranceName,
            EntranceSqid = data.Entrance.EntranceSqid,
            CurrentOccupancy = data.Location.CurrentOccupancy,
            StartingCounter = data.Location.CurrentOccupancy,
            MaxOccupancy = data.Location.MaxOccupancy,
            OccupancyThresholdWarning = data.Location.OccupancyThresholdWarning
        };
        await transaction.CommitAsync();
        return new TrackerResult(TrackerAccess.Success, result);
    }
}
