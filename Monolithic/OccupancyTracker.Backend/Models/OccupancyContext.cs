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
        public DbSet<MarkdownText> MarkdownText { get; set; }

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
                context.Database.Migrate();

                if (!context.EmailProcessorPointers.Any())
                {
                    context.EmailProcessorPointers.Add(new EmailProcessorPointers()
                    {
                        EmailProcessorQueueId = 0,
                        EmailProcessorPointerDescription = "Default",
                        EmailProcessorPointerName = "Default"
                    });
                    context.SaveChanges();
                }

                if (!context.MarkdownText.Any(x => x.PageName == "privacy" && x.TextIdentifier == "body"))
                {
                    context.MarkdownText.Add(new MarkdownText()
                    {
                        PageName = "privacy",
                        TextIdentifier = "body",
                        MarkdownContent = privacyPolicy,
                        CurrentStatus = Statuses.DataStatus.Active.Id
                    });
                    context.SaveChanges();
                }

                if (!context.MarkdownText.Any(x => x.PageName == "terms" && x.TextIdentifier == "body"))
                {
                    context.MarkdownText.Add(new MarkdownText()
                    {
                        PageName = "terms",
                        TextIdentifier = "body",
                        MarkdownContent = termsOfService,
                        CurrentStatus = Statuses.DataStatus.Active.Id
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

        const string termsOfService = @"**Terms of Service**

**Effective Date:** Secure-Scalable.Solutions, LLC

**1. Introduction**
Welcome to Secure-Scalable.Solutions LLC (“Company”, “we”, “our”, “us”). These Terms of Service (“Terms”) govern your use of our multi-tenant SaaS web application (“Service”) provided by Secure-Scalable.Solutions, LLC. By accessing or using the Service, you agree to be bound by these Terms. If you do not agree with any part of the Terms, you may not use the Service.

**2. Accounts**
- **Account Creation:** To use our Service, you must create an account by providing accurate and complete information. You are responsible for maintaining the confidentiality of your account credentials and for all activities that occur under your account.
- **Account Termination:** We reserve the right to suspend or terminate your account if you violate these Terms or engage in any activity that may harm the Service or its users.

**3. Use of the Service**
- **License:** We grant you a limited, non-exclusive, non-transferable, and revocable license to access and use the Service in accordance with these Terms.
- **Prohibited Activities:** You agree not to:
  - Use the Service for any unlawful purpose or in violation of any local, state, national, or international law.
  - Use the Service to transmit any harmful or disruptive content, including viruses, spam, or malware.
  - Interfere with or disrupt the integrity or performance of the Service.

**4. Data and Privacy**
- **Data Collection:** We collect, use, and share data as described in our Privacy Policy. By using the Service, you agree to our data practices.
- **Compliance:** You are responsible for ensuring that your use of the Service complies with all applicable data protection and privacy laws.

**5. Intellectual Property**
- **Ownership:** All content, features, and functionality of the Service are and will remain the exclusive property of Secure-Scalable.Solutions, LLC and its licensors.
- **Restrictions:** You may not copy, modify, distribute, sell, or lease any part of the Service or its content without our prior written consent.

**6. Limitation of Liability**
To the maximum extent permitted by law, Secure-Scalable.Solutions, LLC and its affiliates, officers, directors, employees, agents, and licensors will not be liable for any indirect, incidental, special, consequential, or punitive damages, or any loss of profits or revenues, whether incurred directly or indirectly, or any loss of data, use, goodwill, or other intangible losses, resulting from:
- Your use of or inability to use the Service;
- Any unauthorized access to or use of our servers and/or any personal information stored therein;
- Any bugs, viruses, trojan horses, or other harmful code that may be transmitted to or through the Service;
- Any errors or omissions in any content or for any loss or damage incurred as a result of the use of any content posted, emailed, transmitted, or otherwise made available through the Service.

**7. Governing Law**
These Terms shall be governed by and construed in accordance with the laws of [Your State/Country], without regard to its conflict of law provisions.

**8. Changes to These Terms**
We may modify these Terms from time to time. We will notify you of any changes by posting the new Terms on this page. Your continued use of the Service after such changes constitutes your acceptance of the new Terms.

**9. Contact Us**
If you have any questions about these Terms, please contact us at occupancytracker@secure-scalable.solutions.";

        const string privacyPolicy = @"**Privacy Policy**

**Effective Date:** 2025-Jan-01

**1. Introduction**
Welcome to Secure-Scalable.Solutions, LLC (“Company”, “we”, “our”, “us”)! We are committed to protecting and respecting your privacy. This Privacy Policy explains how we collect, use, disclose, and safeguard your information when you use our multi-tenant SaaS web application (“Service”). By using the Service, you agree to the collection and use of information in accordance with this policy.

**2. Information We Collect**
- **Personal Data:** When you register for an account, we may collect personal information such as your name, email address, phone number, and billing information.
- **Usage Data:** We automatically collect information about how you access and use the Service, such as the IP address, browser type, and pages viewed.
- **Occupancy Data:** We collect data regarding the number of occupants in buildings to ensure compliance with local occupancy laws.

**3. How We Use Your Information**
We use the collected information for various purposes:
- To provide and maintain the Service
- To notify you about changes to the Service
- To provide customer support
- To monitor the usage of the Service
- To detect, prevent, and address technical issues
- To ensure compliance with local occupancy laws

**4. Sharing Your Information**
We do not sell, trade, or otherwise transfer your personal data to outside parties except as described in this Privacy Policy:
- **Service Providers:** We may share your information with third-party service providers who perform services on our behalf, such as payment processing and data analysis.
- **Legal Requirements:** We may disclose your information if required to do so by law or in response to valid requests by public authorities.

**5. Data Security**
We use administrative, technical, and physical security measures to protect your personal information. However, no method of transmission over the internet or method of electronic storage is 100% secure, and we cannot guarantee absolute security.

**6. Your Data Protection Rights**
Depending on your location, you may have the following data protection rights:
- The right to access, update, or delete your personal information
- The right to object to or restrict our processing of your data
- The right to data portability
- The right to withdraw consent

**7. Changes to This Privacy Policy**
We may update our Privacy Policy from time to time. We will notify you of any changes by posting the new Privacy Policy on this page. Changes are effective when they are posted on this page.

**8. Contact Us**
If you have any questions about this Privacy Policy, please contact us at occupancytracker@secure-scalable.solutions.";
    }
}

