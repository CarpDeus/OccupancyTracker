using Microsoft.EntityFrameworkCore;
using OccupancyTracker.Models;
using System.Timers;

namespace OccupancyTracker.Service
{
    public class OccupancyLogProcessor : IDisposable
    {
        private readonly IDbContextFactory<OccupancyContext> _contextFactory;
        private System.Timers.Timer _timer;
        private bool _running;

        public OccupancyLogProcessor(IDbContextFactory<OccupancyContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public void StartExecuting()
        {
            if (!_running)
            {
                // Initiate a Timer
                _timer = new System.Timers.Timer
                {
                    Interval = 60_000,  // every 1 min
                    AutoReset = true,
                    Enabled = true
                };
                _timer.Elapsed += HandleTimer;
                _running = true;
            }
        }

        private async void HandleTimer(object source, ElapsedEventArgs e)
        {
            using var context = _contextFactory.CreateDbContext();
            DateTime start = DateTime.MinValue;
            DateTime end = DateTime.Now.AddMinutes(-1);
            var lastSummary = await context.OccupancyLogSummaries
                .OrderByDescending(s => s.OccupancyLogSummaryId)
                .FirstOrDefaultAsync();

            if (lastSummary != null)
            {
                start = new DateTime(lastSummary.LoggedYear, lastSummary.LoggedMonth, lastSummary.LoggedDay, lastSummary.LoggedHour, lastSummary.LoggedMinute, 0);
            }

            var logs = await context.OccupancyLogs
                .Where(lg => lg.CreatedDate > start && lg.CreatedDate < end)
                .GroupBy(l => new { l.OrganizationId, l.LocationId, l.EntranceId, l.CreatedDate.Year, l.CreatedDate.Month, l.CreatedDate.Day, l.CreatedDate.Hour, l.CreatedDate.Minute })
                .Select(l => new OccupancyLogSummary
                {
                    OrganizationId = l.Key.OrganizationId,
                    LocationId = l.Key.LocationId,
                    EntranceId = l.Key.EntranceId,
                    LoggedYear = l.Key.Year,
                    LoggedMonth = l.Key.Month,
                    LoggedDay = l.Key.Day,
                    LoggedHour = l.Key.Hour,
                    LoggedMinute = l.Key.Minute,
                    EnteredLocation = l.Sum(lg => lg.LoggedChange > 0 ? lg.LoggedChange : 0),
                    ExitedLocation = l.Sum(lg => lg.LoggedChange < 0 ? -lg.LoggedChange : 0),
                    MinOccupancy = l.Min(l => l.CurrentOccupancy),
                    MaxOccupancy = l.Max(l => l.CurrentOccupancy)
                }).ToListAsync();

            // Process the logs
            foreach (var log in logs)
            {
                var sumLog = await context.OccupancyLogSummaries
                    .FirstOrDefaultAsync(s => s.OrganizationId == log.OrganizationId && s.LocationId == log.LocationId && s.EntranceId == log.EntranceId && s.LoggedYear == log.LoggedYear && s.LoggedMonth == log.LoggedMonth && s.LoggedDay == log.LoggedDay && s.LoggedHour == log.LoggedHour && s.LoggedMinute == log.LoggedMinute);

                if (sumLog != null)
                {
                    sumLog.EnteredLocation = log.EnteredLocation;
                    sumLog.ExitedLocation = log.ExitedLocation;
                    sumLog.MinOccupancy = log.MinOccupancy;
                    sumLog.MaxOccupancy = log.MaxOccupancy;
                }
                else
                {
                    context.OccupancyLogSummaries.Add(log);
                }
            }
            await context.SaveChangesAsync();
        }

        public void Dispose()
        {
            if (_running)
            {
                _running = false;
                _timer.Stop();
                _timer.Dispose();
            }
        }
    }
}
