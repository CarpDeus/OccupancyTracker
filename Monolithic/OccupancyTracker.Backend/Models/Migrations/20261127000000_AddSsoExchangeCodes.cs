using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OccupancyTracker.Models.Migrations;

[DbContext(typeof(OccupancyContext))]
[Migration("20261127000000_AddSsoExchangeCodes")]
public sealed class AddSsoExchangeCodes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SsoExchangeCodes",
            columns: table => new
            {
                CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "SQL_Latin1_General_CP1_CS_AS"),
                Challenge = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ProtectedToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_SsoExchangeCodes", x => x.CodeHash));
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "SsoExchangeCodes");
}
