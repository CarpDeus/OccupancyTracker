using Microsoft.EntityFrameworkCore;
using OccupancyTracker.IRepository;
using OccupancyTracker.Models;

namespace OccupancyTracker.Repository;

public sealed class DatabaseMigrator(IDbContextFactory<OccupancyContext> contexts) : IDatabaseMigrator
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }
}
