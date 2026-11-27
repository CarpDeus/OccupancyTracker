using System.Data;
using OccupancyTracker.Models;

namespace OccupancyTracker.IRepository;

public interface IRepositorySession : IDisposable, IAsyncDisposable
{
    ICrudRepository<Location> Locations { get; }
    ICrudRepository<Organization> Organizations { get; }
    ICrudRepository<OrganizationInvitationCodes> OrganizationInvitationCodes { get; }
    ICrudRepository<Entrance> Entrances { get; }
    ICrudRepository<EntranceCounter> EntranceCounters { get; }
    ICrudRepository<OccupancyLog> OccupancyLogs { get; }
    ICrudRepository<OccupancyLogSummary> OccupancyLogSummaries { get; }
    ICrudRepository<OrganizationUser> OrganizationUsers { get; }
    ICrudRepository<OrganizationUserRole> OrganizationUserRoles { get; }
    ICrudRepository<UserSsoInformation> UserSsoInformation { get; }
    ICrudRepository<UserInformation> UserInformation { get; }
    ICrudRepository<InvalidSecurityAttempt> InvalidSecurityAttempt { get; }
    ICrudRepository<EmailProcessorQueue> EmailProcessorQueue { get; }
    ICrudRepository<EmailProcessorHistory> EmailProcessorHistory { get; }
    ICrudRepository<EmailProcessorPointers> EmailProcessorPointers { get; }
    ICrudRepository<MarkdownText> MarkdownText { get; }
    ICrudRepository<SsoExchangeCode> SsoExchangeCodes { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    int SaveChanges();
    Task<IRepositoryTransaction> BeginTransactionAsync(IsolationLevel isolationLevel,
        CancellationToken cancellationToken = default);
}

public interface IRepositoryTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface IRepositorySessionFactory
{
    IRepositorySession Create();
    Task<IRepositorySession> CreateAsync(CancellationToken cancellationToken = default);
    bool IsConcurrencyConflict(Exception exception);
}
