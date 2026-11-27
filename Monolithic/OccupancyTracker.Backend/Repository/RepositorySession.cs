using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OccupancyTracker.IRepository;
using OccupancyTracker.Models;

namespace OccupancyTracker.Repository;

public sealed class RepositorySessionFactory(IDbContextFactory<OccupancyContext> contexts) : IRepositorySessionFactory
{
    public IRepositorySession Create() => new RepositorySession(contexts.CreateDbContext());
    public async Task<IRepositorySession> CreateAsync(CancellationToken cancellationToken = default) =>
        new RepositorySession(await contexts.CreateDbContextAsync(cancellationToken));
    public bool IsConcurrencyConflict(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is Microsoft.Data.SqlClient.SqlException { Number: 1205 }) return true;
        }
        return false;
    }
}

internal sealed class RepositorySession(OccupancyContext context) : IRepositorySession
{
    public ICrudRepository<Location> Locations { get; } = new CrudRepository<Location>(context);
    public ICrudRepository<Organization> Organizations { get; } = new CrudRepository<Organization>(context);
    public ICrudRepository<OrganizationInvitationCodes> OrganizationInvitationCodes { get; } = new CrudRepository<OrganizationInvitationCodes>(context);
    public ICrudRepository<Entrance> Entrances { get; } = new CrudRepository<Entrance>(context);
    public ICrudRepository<EntranceCounter> EntranceCounters { get; } = new CrudRepository<EntranceCounter>(context);
    public ICrudRepository<OccupancyLog> OccupancyLogs { get; } = new CrudRepository<OccupancyLog>(context);
    public ICrudRepository<OccupancyLogSummary> OccupancyLogSummaries { get; } = new CrudRepository<OccupancyLogSummary>(context);
    public ICrudRepository<OrganizationUser> OrganizationUsers { get; } = new CrudRepository<OrganizationUser>(context);
    public ICrudRepository<OrganizationUserRole> OrganizationUserRoles { get; } = new CrudRepository<OrganizationUserRole>(context);
    public ICrudRepository<UserSsoInformation> UserSsoInformation { get; } = new CrudRepository<UserSsoInformation>(context);
    public ICrudRepository<UserInformation> UserInformation { get; } = new CrudRepository<UserInformation>(context);
    public ICrudRepository<InvalidSecurityAttempt> InvalidSecurityAttempt { get; } = new CrudRepository<InvalidSecurityAttempt>(context);
    public ICrudRepository<EmailProcessorQueue> EmailProcessorQueue { get; } = new CrudRepository<EmailProcessorQueue>(context);
    public ICrudRepository<EmailProcessorHistory> EmailProcessorHistory { get; } = new CrudRepository<EmailProcessorHistory>(context);
    public ICrudRepository<EmailProcessorPointers> EmailProcessorPointers { get; } = new CrudRepository<EmailProcessorPointers>(context);
    public ICrudRepository<MarkdownText> MarkdownText { get; } = new CrudRepository<MarkdownText>(context);
    public ICrudRepository<SsoExchangeCode> SsoExchangeCodes { get; } = new CrudRepository<SsoExchangeCode>(context);
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);
    public int SaveChanges() => context.SaveChanges();
    public async Task<IRepositoryTransaction> BeginTransactionAsync(IsolationLevel isolationLevel,
        CancellationToken cancellationToken = default) =>
        new RepositoryTransaction(await context.Database.BeginTransactionAsync(isolationLevel, cancellationToken));
    public void Dispose() => context.Dispose();
    public ValueTask DisposeAsync() => context.DisposeAsync();

    private sealed class RepositoryTransaction(IDbContextTransaction transaction) : IRepositoryTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
