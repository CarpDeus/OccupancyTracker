using System.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using OccupancyTracker.Models;
using OccupancyTracker.Repository;

namespace OccupancyTracker.Tests.Services;

public sealed class RepositoryTests
{
    [Test]
    public async Task Crud_PersistsChangesWithoutApplyingStatusOrAuthorizationRules()
    {
        using var factory = new ContextFactory();
        var repositories = new RepositorySessionFactory(factory);
        long id;
        await using (var session = await repositories.CreateAsync())
        {
            var organization = new Organization { OrganizationName = "Inactive", CurrentStatus = 2 };
            session.Organizations.Add(organization);
            await session.SaveChangesAsync();
            id = organization.OrganizationId;
        }
        await using (var session = await repositories.CreateAsync())
        {
            var organization = await session.Organizations.SingleAsync(x => x.OrganizationId == id);
            organization.CurrentStatus.Should().Be(2);
            organization.OrganizationName = "Updated";
            await session.SaveChangesAsync();
        }
        await using (var session = await repositories.CreateAsync())
        {
            var organization = await session.Organizations.SingleAsync(x => x.OrganizationId == id);
            organization.OrganizationName.Should().Be("Updated");
            session.Organizations.Remove(organization);
            await session.SaveChangesAsync();
            (await session.Organizations.AnyAsync(x => x.OrganizationId == id)).Should().BeFalse();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Session_TransactionControlsWritesAcrossRepositories(bool commit)
    {
        using var factory = new ContextFactory();
        var repositories = new RepositorySessionFactory(factory);
        await using (var session = await repositories.CreateAsync())
        {
            await using var transaction = await session.BeginTransactionAsync(IsolationLevel.Serializable);
            var organization = new Organization();
            session.Organizations.Add(organization);
            await session.SaveChangesAsync();
            session.Locations.Add(new Location { OrganizationId = organization.OrganizationId });
            await session.SaveChangesAsync();
            if (commit) await transaction.CommitAsync();
        }
        await using var check = await repositories.CreateAsync();
        (await check.Organizations.AnyAsync()).Should().Be(commit);
        (await check.Locations.AnyAsync()).Should().Be(commit);
    }

    [Test]
    public async Task DetachedActorProjection_IsNotSavedWithOtherTrackedChanges()
    {
        using var factory = new ContextFactory();
        var repositories = new RepositorySessionFactory(factory);
        await using (var session = await repositories.CreateAsync())
        {
            session.UserInformation.Add(new UserInformation { FirstName = "Alice", LastName = "User", EmailAddress = "alice@example.com" });
            await session.SaveChangesAsync();
        }
        await using (var session = await repositories.CreateAsync())
        {
            var actor = await session.UserInformation.SingleOrDefaultDetachedAsync(x => x.EmailAddress == "alice@example.com");
            actor!.IsSuperAdmin = true;
            session.Organizations.Add(new Organization());
            await session.SaveChangesAsync();
        }
        await using var check = await repositories.CreateAsync();
        (await check.UserInformation.SingleAsync(x => x.EmailAddress == "alice@example.com")).IsSuperAdmin.Should().BeFalse();
    }

    private sealed class ContextFactory : IDbContextFactory<OccupancyContext>, IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly DbContextOptions<OccupancyContext> options;

        public ContextFactory()
        {
            connection.Open();
            connection.CreateCollation("SQL_Latin1_General_CP1_CS_AS", string.CompareOrdinal);
            options = new DbContextOptionsBuilder<OccupancyContext>().UseSqlite(connection).Options;
            using var context = CreateDbContext();
            context.Database.EnsureCreated();
        }

        public OccupancyContext CreateDbContext() => new(options);
        public void Dispose() => connection.Dispose();
    }
}
