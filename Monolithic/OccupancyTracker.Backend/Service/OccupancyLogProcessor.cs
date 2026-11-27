using OccupancyTracker.IRepository;
using OccupancyTracker.Models;
using System.Timers;

namespace OccupancyTracker.Service
{
    /// <summary>
    /// Background service to process occupancy logs and generate summaries.
    /// </summary>
    public class OccupancyLogProcessor : BackgroundService
    {
        private readonly IRepositorySessionFactory _contextFactory;
        private PeriodicTimer _timer = new PeriodicTimer(new TimeSpan(0, 0, 30));

        /// <summary>
        /// Initializes a new instance of the <see cref="OccupancyLogProcessor"/> class.
        /// </summary>
        /// <param name="contextFactory">The factory to create repository sessions.</param>
        public OccupancyLogProcessor(IRepositorySessionFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        /// <summary>
        /// Executes the background service to process occupancy logs.
        /// </summary>
        /// <param name="stoppingToken">Token to monitor for cancellation requests.</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (await _timer.WaitForNextTickAsync(stoppingToken)
                && !stoppingToken.IsCancellationRequested)
            {
                using var context = _contextFactory.Create();
                DateTime start = DateTime.MinValue;
                DateTime end = DateTime.Now.AddMinutes(-1);
                var lastSummary = await context.OccupancyLogSummaries.FirstOrDefaultAsync(
                    s => true, s => s.OccupancyLogSummaryId, descending: true, cancellationToken: stoppingToken);

                if (lastSummary != null)
                {
                    start = new DateTime(lastSummary.LoggedYear, lastSummary.LoggedMonth, lastSummary.LoggedDay, lastSummary.LoggedHour, lastSummary.LoggedMinute, 0);
                }

                var entries = await context.OccupancyLogs.ListAsync(lg => lg.CreatedDate > start && lg.CreatedDate < end,
                    stoppingToken);
                var logs = entries
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
                    }).ToList();

                // Process the logs
                foreach (var log in logs)
                {
                    var sumLog = await context.OccupancyLogSummaries.FirstOrDefaultAsync(
                        s => s.OrganizationId == log.OrganizationId && s.LocationId == log.LocationId && s.EntranceId == log.EntranceId && s.LoggedYear == log.LoggedYear && s.LoggedMonth == log.LoggedMonth && s.LoggedDay == log.LoggedDay && s.LoggedHour == log.LoggedHour && s.LoggedMinute == log.LoggedMinute,
                        stoppingToken);

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
        }
    }
}
