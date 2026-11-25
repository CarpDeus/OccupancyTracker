using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OccupancyTracker.Models.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailProcessorHistory",
                columns: table => new
                {
                    EmailProcessorHistoryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmailProcessorPointersId = table.Column<long>(type: "bigint", nullable: false),
                    EmailProcessorQueueId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrentStatus = table.Column<short>(type: "smallint", nullable: false),
                    OtherInformation = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailProcessorHistory", x => x.EmailProcessorHistoryId);
                });

            migrationBuilder.CreateTable(
                name: "EmailProcessorPointers",
                columns: table => new
                {
                    EmailProcessorPointersId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmailProcessorPointerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmailProcessorPointerDescription = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmailProcessorQueueId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailProcessorPointers", x => x.EmailProcessorPointersId);
                });

            migrationBuilder.CreateTable(
                name: "EmailProcessorQueue",
                columns: table => new
                {
                    EmailProcessorQueueId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmailProcessorData = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailProcessorQueue", x => x.EmailProcessorQueueId);
                });

            migrationBuilder.CreateTable(
                name: "EntranceCounters",
                columns: table => new
                {
                    EntranceCounterId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntranceId = table.Column<long>(type: "bigint", nullable: false),
                    LocationId = table.Column<long>(type: "bigint", nullable: false),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    RequiresAuthentication = table.Column<bool>(type: "bit", nullable: false),
                    EntranceCounterSqid = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "SQL_Latin1_General_CP1_CS_AS"),
                    CurrentStatus = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EntranceName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntranceCounters", x => x.EntranceCounterId);
                });

            migrationBuilder.CreateTable(
                name: "Entrances",
                columns: table => new
                {
                    EntranceId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntranceName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EntranceDescription = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EntranceSqid = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "SQL_Latin1_General_CP1_CS_AS"),
                    EntranceCounterInstanceSqid = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "SQL_Latin1_General_CP1_CS_AS"),
                    CurrentStatus = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LocationId = table.Column<long>(type: "bigint", nullable: false),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entrances", x => x.EntranceId);
                });

            migrationBuilder.CreateTable(
                name: "InvalidSecurityAttempt",
                columns: table => new
                {
                    InvalidSecurityAttemptId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserInformationId = table.Column<long>(type: "bigint", nullable: false),
                    InvalidSecurityAttemptSqid = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AttemptLogged = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: true),
                    LocationId = table.Column<long>(type: "bigint", nullable: true),
                    EntranceId = table.Column<long>(type: "bigint", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdditionalAttemptInformation = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvalidSecurityAttempt", x => x.InvalidSecurityAttemptId);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    LocationId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LocationName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    LocationDescription = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MaxOccupancy = table.Column<int>(type: "int", nullable: false),
                    CurrentOccupancy = table.Column<int>(type: "int", nullable: false),
                    OccupancyThresholdWarning = table.Column<int>(type: "int", nullable: false),
                    CurrentStatus = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LocationSqid = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "SQL_Latin1_General_CP1_CS_AS"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    EntranceCount = table.Column<int>(type: "int", nullable: false),
                    LocationAddress_AddressLine1 = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    LocationAddress_AddressLine2 = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    LocationAddress_City = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    LocationAddress_Country = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LocationAddress_PostalCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LocationAddress_State = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PhoneNumber_CountryCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber_Number = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.LocationId);
                });

            migrationBuilder.CreateTable(
                name: "OccupancyLogs",
                columns: table => new
                {
                    OccupancyLogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    LocationId = table.Column<long>(type: "bigint", nullable: false),
                    EntranceId = table.Column<long>(type: "bigint", nullable: false),
                    EntranceCounterId = table.Column<long>(type: "bigint", nullable: false),
                    LoggedChange = table.Column<int>(type: "int", nullable: false),
                    CurrentOccupancy = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OccupancyLogs", x => x.OccupancyLogId);
                });

            migrationBuilder.CreateTable(
                name: "OccupancyLogSummaries",
                columns: table => new
                {
                    OccupancyLogSummaryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    LocationId = table.Column<long>(type: "bigint", nullable: false),
                    EntranceId = table.Column<long>(type: "bigint", nullable: false),
                    LoggedYear = table.Column<int>(type: "int", nullable: false),
                    LoggedMonth = table.Column<int>(type: "int", nullable: false),
                    LoggedDay = table.Column<int>(type: "int", nullable: false),
                    LoggedHour = table.Column<int>(type: "int", nullable: false),
                    LoggedMinute = table.Column<int>(type: "int", nullable: false),
                    EnteredLocation = table.Column<int>(type: "int", nullable: false),
                    ExitedLocation = table.Column<int>(type: "int", nullable: false),
                    MinOccupancy = table.Column<int>(type: "int", nullable: false),
                    MaxOccupancy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OccupancyLogSummaries", x => x.OccupancyLogSummaryId);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationInvitationCodes",
                columns: table => new
                {
                    OrganizationInvitationCodeId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    EmailAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PostRegistrationRoleInformation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InvitationCode = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true, collation: "SQL_Latin1_General_CP1_CS_AS"),
                    CurrentStatus = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    InvitationRedeemed = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationInvitationCodes", x => x.OrganizationInvitationCodeId);
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OrganizationDescription = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    PaidClient = table.Column<bool>(type: "bit", nullable: false),
                    PaidThroughDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OrganizationSqid = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true, collation: "SQL_Latin1_General_CP1_CS_AS"),
                    CurrentStatus = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LocationCount = table.Column<int>(type: "int", nullable: false),
                    DefaultRoleId = table.Column<int>(type: "int", nullable: false),
                    OrganizationAddress_AddressLine1 = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    OrganizationAddress_AddressLine2 = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    OrganizationAddress_City = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    OrganizationAddress_Country = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    OrganizationAddress_PostalCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OrganizationAddress_State = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PhoneNumber_CountryCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber_Number = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.OrganizationId);
                });

            migrationBuilder.CreateTable(
                name: "UserInformation",
                columns: table => new
                {
                    UserInformationId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserSsoInformationIdLastLoggedIn = table.Column<long>(type: "bigint", nullable: false),
                    EmailAddress = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserInformationSqid = table.Column<string>(type: "nvarchar(max)", nullable: true, collation: "SQL_Latin1_General_CP1_CS_AS"),
                    IsSuperAdmin = table.Column<bool>(type: "bit", nullable: false),
                    HasCompletedRegistration = table.Column<bool>(type: "bit", nullable: false),
                    BelongsToOrganization = table.Column<bool>(type: "bit", nullable: false),
                    CurrentStatusId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", maxLength: 450, nullable: false),
                    ModifiedBy = table.Column<long>(type: "bigint", maxLength: 450, nullable: true),
                    ContactAddress_AddressLine1 = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ContactAddress_AddressLine2 = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ContactAddress_City = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ContactAddress_Country = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ContactAddress_PostalCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ContactAddress_State = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ContactPhoneNumber_CountryCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContactPhoneNumber_Number = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserInformation", x => x.UserInformationId);
                });

            migrationBuilder.CreateTable(
                name: "UserSsoInformation",
                columns: table => new
                {
                    UserSsoInformationId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserInformationId = table.Column<long>(type: "bigint", nullable: true),
                    UserSsoInformationIdLastLoggedIn = table.Column<long>(type: "bigint", nullable: false),
                    UserLastLoggedIn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Auth0Identifier = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GivenName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Surname = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Picture = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmailAddress = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSsoInformation", x => x.UserSsoInformationId);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationUserRoles",
                columns: table => new
                {
                    OrganizationUserRoleId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrganizationWide = table.Column<bool>(type: "bit", nullable: false),
                    OrganizationUserId = table.Column<long>(type: "bigint", nullable: false),
                    LocationId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationUserRoles", x => x.OrganizationUserRoleId);
                    table.ForeignKey(
                        name: "FK_OrganizationUserRoles_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "LocationId");
                });

            migrationBuilder.CreateTable(
                name: "OrganizationUsers",
                columns: table => new
                {
                    OrganizationUsersId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserInformationId = table.Column<long>(type: "bigint", nullable: false),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    UserStatusId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationUsers", x => x.OrganizationUsersId);
                    table.ForeignKey(
                        name: "FK_OrganizationUsers_UserInformation_UserInformationId",
                        column: x => x.UserInformationId,
                        principalTable: "UserInformation",
                        principalColumn: "UserInformationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUserRoles_LocationId",
                table: "OrganizationUserRoles",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUsers_UserInformationId",
                table: "OrganizationUsers",
                column: "UserInformationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailProcessorHistory");

            migrationBuilder.DropTable(
                name: "EmailProcessorPointers");

            migrationBuilder.DropTable(
                name: "EmailProcessorQueue");

            migrationBuilder.DropTable(
                name: "EntranceCounters");

            migrationBuilder.DropTable(
                name: "Entrances");

            migrationBuilder.DropTable(
                name: "InvalidSecurityAttempt");

            migrationBuilder.DropTable(
                name: "OccupancyLogs");

            migrationBuilder.DropTable(
                name: "OccupancyLogSummaries");

            migrationBuilder.DropTable(
                name: "OrganizationInvitationCodes");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropTable(
                name: "OrganizationUserRoles");

            migrationBuilder.DropTable(
                name: "OrganizationUsers");

            migrationBuilder.DropTable(
                name: "UserSsoInformation");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "UserInformation");
        }
    }
}
