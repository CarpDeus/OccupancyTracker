using OccupancyTracker.Models;

public sealed class DatabaseInitializationService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<OccupancyDbInitializer>().Run();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
