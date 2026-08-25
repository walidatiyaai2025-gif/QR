using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SecureQrPortal.Data;

#nullable disable
namespace SecureQrPortal.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("202608250002_QrShareLinks")]
public sealed class QrShareLinks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)) migrationBuilder.Sql(SqliteSql);
        else if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase)) migrationBuilder.Sql(SqlServerSql);
        else throw new NotSupportedException($"Unsupported database provider {ActiveProvider}");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)) migrationBuilder.Sql("DROP TABLE IF EXISTS \"QrShareLinks\";");
        else migrationBuilder.Sql("IF OBJECT_ID(N'[QrShareLinks]', N'U') IS NOT NULL DROP TABLE [QrShareLinks];");
    }

    private const string SqliteSql = """
CREATE TABLE "QrShareLinks" (
    "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    "SecurePageId" INTEGER NOT NULL,
    "TokenHash" TEXT NOT NULL,
    "ProtectedToken" TEXT NOT NULL,
    "Username" TEXT NOT NULL,
    "PasswordHash" TEXT NOT NULL,
    "ProtectedPassword" TEXT NOT NULL,
    "MaxOpenCount" INTEGER NOT NULL,
    "CurrentOpenCount" INTEGER NOT NULL,
    "SessionDurationMinutes" INTEGER NOT NULL,
    "ExpiresAtUtc" TEXT NOT NULL,
    "FirstOpenedAtUtc" TEXT NULL,
    "LastOpenedAtUtc" TEXT NULL,
    "AccessWindowEndsAtUtc" TEXT NULL,
    "RevokedAtUtc" TEXT NULL,
    "CreatedByAdminId" TEXT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    FOREIGN KEY ("SecurePageId") REFERENCES "SecurePages" ("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX "IX_QrShareLinks_TokenHash" ON "QrShareLinks" ("TokenHash");
CREATE INDEX "IX_QrShareLinks_SecurePageId_CreatedAtUtc" ON "QrShareLinks" ("SecurePageId", "CreatedAtUtc");
""";

    private const string SqlServerSql = """
CREATE TABLE [QrShareLinks] (
    [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [SecurePageId] bigint NOT NULL,
    [TokenHash] nvarchar(64) NOT NULL,
    [ProtectedToken] nvarchar(max) NOT NULL,
    [Username] nvarchar(150) NOT NULL,
    [PasswordHash] nvarchar(max) NOT NULL,
    [ProtectedPassword] nvarchar(max) NOT NULL,
    [MaxOpenCount] int NOT NULL,
    [CurrentOpenCount] int NOT NULL,
    [SessionDurationMinutes] int NOT NULL,
    [ExpiresAtUtc] datetime2 NOT NULL,
    [FirstOpenedAtUtc] datetime2 NULL,
    [LastOpenedAtUtc] datetime2 NULL,
    [AccessWindowEndsAtUtc] datetime2 NULL,
    [RevokedAtUtc] datetime2 NULL,
    [CreatedByAdminId] nvarchar(450) NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [FK_QrShareLinks_SecurePages_SecurePageId] FOREIGN KEY ([SecurePageId]) REFERENCES [SecurePages] ([Id]) ON DELETE CASCADE
);
CREATE UNIQUE INDEX [IX_QrShareLinks_TokenHash] ON [QrShareLinks] ([TokenHash]);
CREATE INDEX [IX_QrShareLinks_SecurePageId_CreatedAtUtc] ON [QrShareLinks] ([SecurePageId], [CreatedAtUtc]);
""";
}
