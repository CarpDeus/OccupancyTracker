using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;

namespace OccupancyTracker.Models
{
    public class OccupancyContext : DbContext
    {

        //entities 
        public DbSet<Location> Locations { get; set; }
        public DbSet<Organization> Organizations { get; set; }
        public DbSet<OrganizationInvitationCodes> OrganizationInvitationCodes { get; set; }
        public DbSet<Entrance> Entrances { get; set; }
        public DbSet<EntranceCounter> EntranceCounters { get; set; }
        public DbSet<OccupancyLog> OccupancyLogs { get; set; }
        public DbSet<OccupancyLogSummary> OccupancyLogSummaries { get; set; }
        //public DbSet<Profile> Profiles { get; set; }
        public DbSet<OrganizationUser> OrganizationUsers { get; set; }
        public DbSet<OrganizationUserRole> OrganizationUserRoles { get; set; }
        public DbSet<UserSsoInformation> UserSsoInformation { get; set; }
        public DbSet<UserInformation> UserInformation { get; set; }
        public DbSet<InvalidSecurityAttempt> InvalidSecurityAttempt { get; set; }
        public DbSet<EmailProcessorQueue> EmailProcessorQueue { get; set; }
        public DbSet<EmailProcessorHistory> EmailProcessorHistory { get; set; }
        public DbSet<EmailProcessorPointers> EmailProcessorPointers { get; set; }

        public OccupancyContext(DbContextOptions<OccupancyContext> options)
            : base(options)
        {

        }

        // Define the model.
        // modelBuilder: The ModelBuilder.
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // SQID Fields need to be case sensitive
            modelBuilder.Entity<Location>().Property(c => c.LocationSqid).UseCollation("SQL_Latin1_General_CP1_CS_AS");
            modelBuilder.Entity<Organization>().Property(c => c.OrganizationSqid).UseCollation("SQL_Latin1_General_CP1_CS_AS");
            modelBuilder.Entity<Entrance>().Property(c => c.EntranceSqid).UseCollation("SQL_Latin1_General_CP1_CS_AS");
            modelBuilder.Entity<Entrance>().Property(c => c.EntranceCounterInstanceSqid).UseCollation("SQL_Latin1_General_CP1_CS_AS");
            modelBuilder.Entity<EntranceCounter>().Property(c => c.EntranceCounterSqid).UseCollation("SQL_Latin1_General_CP1_CS_AS");
            modelBuilder.Entity<OrganizationInvitationCodes>().Property(c => c.InvitationCode).UseCollation("SQL_Latin1_General_CP1_CS_AS");
            modelBuilder.Entity<UserInformation>().Property(c => c.UserInformationSqid).UseCollation("SQL_Latin1_General_CP1_CS_AS");

            // Relationships
            //modelBuilder.Entity<Location>().HasOne(p => p.Organization).WithMany(b => b.Location).HasForeignKey(p => p.OrganizationId);
            //modelBuilder.Entity<Location>().HasOne(p => p.Organization).WithMany(b => b.Locations).HasForeignKey(p => p.OrganizationId);
            //modelBuilder.Entity<Entrance>().HasOne(p => p.Location).WithMany(b => b.Entrances).HasForeignKey(p => p.Location.LocationId);


            base.OnModelCreating(modelBuilder);
        }
    }
    public class OccupancyDbInitializer
    {
        private readonly IDbContextFactory<OccupancyContext> _contextFactory;


        public OccupancyDbInitializer(IDbContextFactory<OccupancyContext> contextFactory)
        {
            _contextFactory = contextFactory;
            
        }

        public void Run()
        {
            using (var context = _contextFactory.CreateDbContext())
            {
                context.Database.EnsureCreated();

                if (!context.EmailProcessorPointers.Any())
                {
                    context.EmailProcessorPointers.Add(new EmailProcessorPointers()
                    {
                        EmailProcessorQueueId = 1,
                        EmailProcessorPointerDescription = "Default",
                        EmailProcessorPointerName = "Default"
                    });
                    context.SaveChanges();
                }

                //// Look for any Locations.
                //if (context.Locations.Any())
                //{
                //    return;   // DB has been seeded
                //}
            }

        }
    }
}

