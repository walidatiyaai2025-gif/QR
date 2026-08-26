using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SecureQrPortal.Data;

#nullable disable
namespace SecureQrPortal.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("202608260003_SecureMessageEncryptionControl")]
public sealed class SecureMessageEncryptionControl : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ProtectedContentKey",
            table: "SecurePages",
            nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "ContentEncryptionVersion",
            table: "SecurePages",
            nullable: false,
            defaultValue: 0);
        migrationBuilder.AddColumn<DateTime>(
            name: "ContentKeyDestroyedAtUtc",
            table: "SecurePages",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ProtectedContentKey", table: "SecurePages");
        migrationBuilder.DropColumn(name: "ContentEncryptionVersion", table: "SecurePages");
        migrationBuilder.DropColumn(name: "ContentKeyDestroyedAtUtc", table: "SecurePages");
    }
}
