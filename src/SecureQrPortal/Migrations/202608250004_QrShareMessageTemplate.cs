using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SecureQrPortal.Data;

#nullable disable
namespace SecureQrPortal.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("202608250004_QrShareMessageTemplate")]
public sealed class QrShareMessageTemplate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            migrationBuilder.Sql("ALTER TABLE \"QrShareLinks\" ADD COLUMN \"MessageTemplate\" TEXT NULL;");
        else if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            migrationBuilder.Sql("ALTER TABLE [QrShareLinks] ADD [MessageTemplate] nvarchar(2000) NULL;");
        else
            throw new NotSupportedException($"Unsupported database provider {ActiveProvider}");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            migrationBuilder.Sql("ALTER TABLE \"QrShareLinks\" DROP COLUMN \"MessageTemplate\";");
        else if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            migrationBuilder.Sql("ALTER TABLE [QrShareLinks] DROP COLUMN [MessageTemplate];");
        else
            throw new NotSupportedException($"Unsupported database provider {ActiveProvider}");
    }
}
