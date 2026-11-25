using System.Data;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.DTO;
using OccupancyTracker.Models;

namespace OccupancyTracker.Service;

public enum TrackerAccess { Success, NotFound, Unauthenticated, Forbidden }
public record TrackerResult(TrackerAccess Access, EntranceCounterDto? Counter = null);

public sealed class TrackerOperations(
    IDbContextFactory<OccupancyContext> contextFactory,
    RequestAuthorizationService authorization,
    IHttpContextAccessor httpContextAccessor)
{
    public Task<TrackerResult> ReadAsync(string counterSqid) => ExecuteAsync(counterSqid, null);
    public Task<TrackerResult> UpdateAsync(string counterSqid, int delta) => ExecuteAsync(counterSqid, delta);

    private async Task<TrackerResult> ExecuteAsync(string counterSqid, int? delta)
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
                return await ExecuteOnceAsync(counterSqid, delta);
            }
            catch (Exception ex) when (attempt < maxAttempts && IsDeadlock(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt));
            }
        }
    }

    private async Task<TrackerResult> ExecuteOnceAsync(string counterSqid, int? delta)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var data = await (from counter in context.EntranceCounters
            join entrance in context.Entrances on counter.EntranceId equals entrance.EntranceId
            join location in context.Locations on counter.LocationId equals location.LocationId
            join organization in context.Organizations on counter.OrganizationId equals organization.OrganizationId
            where counter.EntranceCounterSqid == counterSqid &&
                entrance.EntranceCounterInstanceSqid == counterSqid &&
                entrance.LocationId == location.LocationId && entrance.OrganizationId == organization.OrganizationId &&
                location.OrganizationId == organization.OrganizationId &&
                counter.CurrentStatus == 0 && entrance.CurrentStatus == 0 && location.CurrentStatus == 0 &&
                organization.CurrentStatus == 0
            select new { counter, entrance, location, organization }).SingleOrDefaultAsync();
        if (data == null)
            return new TrackerResult(TrackerAccess.NotFound);

        if (!await authorization.CanUseCounterAsync(context, counterSqid))
            return new TrackerResult(httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true
                ? TrackerAccess.Forbidden : TrackerAccess.Unauthenticated);

        if (delta.HasValue)
        {
            data.location.CurrentOccupancy = checked(data.location.CurrentOccupancy + delta.Value);
            context.OccupancyLogs.Add(new OccupancyLog
            {
                OrganizationId = data.organization.OrganizationId,
                LocationId = data.location.LocationId,
                EntranceId = data.entrance.EntranceId,
                EntranceCounterId = data.counter.EntranceCounterId,
                LoggedChange = delta.Value,
                CurrentOccupancy = data.location.CurrentOccupancy,
                CreatedDate = DateTime.Now
            });
            await context.SaveChangesAsync();
        }

        var result = new EntranceCounterDto(data.counter)
        {
            OrganizationName = data.organization.OrganizationName,
            OrganizationSqid = data.organization.OrganizationSqid,
            LocationName = data.location.LocationName,
            LocationSqid = data.location.LocationSqid,
            EntranceName = data.entrance.EntranceName,
            EntranceSqid = data.entrance.EntranceSqid,
            CurrentOccupancy = data.location.CurrentOccupancy,
            StartingCounter = data.location.CurrentOccupancy,
            MaxOccupancy = data.location.MaxOccupancy,
            OccupancyThresholdWarning = data.location.OccupancyThresholdWarning
        };
        await transaction.CommitAsync();
        return new TrackerResult(TrackerAccess.Success, result);
    }
}
