using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SecureQrPortal.Data;

#nullable disable
namespace SecureQrPortal.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("202608250003_QrShareRevealIdempotency")]
public sealed class QrShareRevealIdempotency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            migrationBuilder.Sql("ALTER TABLE \"QrShareLinks\" ADD COLUMN \"LastRevealRequestHash\" TEXT NULL;");
        else if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            migrationBuilder.Sql("ALTER TABLE [QrShareLinks] ADD [LastRevealRequestHash] nvarchar(64) NULL;");
        else
            throw new NotSupportedException($"Unsupported database provider {ActiveProvider}");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            // SQLite DROP COLUMN requires modern SQLite, which is provided by the app runtime.
            migrationBuilder.Sql("ALTER TABLE \"QrShareLinks\" DROP COLUMN \"LastRevealRequestHash\";");
        }
        else if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            migrationBuilder.Sql("ALTER TABLE [QrShareLinks] DROP COLUMN [LastRevealRequestHash];");
        else
            throw new NotSupportedException($"Unsupported database provider {ActiveProvider}");
    }
}
