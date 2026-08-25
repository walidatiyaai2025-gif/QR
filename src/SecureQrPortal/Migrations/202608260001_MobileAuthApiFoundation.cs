using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SecureQrPortal.Data;

#nullable disable
namespace SecureQrPortal.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("202608260001_MobileAuthApiFoundation")]
public sealed class MobileAuthApiFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "MobileNumber",
            table: "Organizations",
            maxLength: 11,
            nullable: true);

        if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_Organizations_MobileNumber\" ON \"Organizations\" (\"MobileNumber\") WHERE \"MobileNumber\" IS NOT NULL;");
        else if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            migrationBuilder.Sql("CREATE UNIQUE INDEX [IX_Organizations_MobileNumber] ON [Organizations] ([MobileNumber]) WHERE [MobileNumber] IS NOT NULL;");
        else
            throw new NotSupportedException($"Unsupported database provider {ActiveProvider}");

        migrationBuilder.CreateTable(
            name: "MobileOtpChallenges",
            columns: table => new
            {
                Id = table.Column<long>(nullable: false)
                    .Annotation("Sqlite:Autoincrement", true)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ChallengeId = table.Column<string>(maxLength: 64, nullable: false),
                OrganizationId = table.Column<long>(nullable: false),
                MobileNumber = table.Column<string>(maxLength: 11, nullable: false),
                OtpHash = table.Column<string>(maxLength: 64, nullable: false),
                ProtectedVerificationKey = table.Column<string>(nullable: false),
                CreatedAtUtc = table.Column<DateTime>(nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(nullable: false),
                ResendAvailableAtUtc = table.Column<DateTime>(nullable: false),
                AttemptCount = table.Column<int>(nullable: false),
                MaxAttempts = table.Column<int>(nullable: false),
                ConsumedAtUtc = table.Column<DateTime>(nullable: true),
                RevokedAtUtc = table.Column<DateTime>(nullable: true),
                ProviderSucceeded = table.Column<bool>(nullable: false),
                ProviderHttpStatusCode = table.Column<int>(nullable: true),
                ProviderResultCode = table.Column<string>(maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MobileOtpChallenges", x => x.Id);
                table.ForeignKey(
                    name: "FK_MobileOtpChallenges_Organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "Organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "MobileSessions",
            columns: table => new
            {
                Id = table.Column<long>(nullable: false)
                    .Annotation("Sqlite:Autoincrement", true)
                    .Annotation("SqlServer:Identity", "1, 1"),
                SessionId = table.Column<string>(maxLength: 64, nullable: false),
                OrganizationId = table.Column<long>(nullable: false),
                AccessTokenHash = table.Column<string>(maxLength: 64, nullable: false),
                RefreshTokenHash = table.Column<string>(maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(nullable: false),
                AccessExpiresAtUtc = table.Column<DateTime>(nullable: false),
                RefreshExpiresAtUtc = table.Column<DateTime>(nullable: false),
                RefreshUsedAtUtc = table.Column<DateTime>(nullable: true),
                RevokedAtUtc = table.Column<DateTime>(nullable: true),
                ReplacedBySessionId = table.Column<string>(maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MobileSessions", x => x.Id);
                table.ForeignKey(
                    name: "FK_MobileSessions_Organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "Organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "MobileDevices",
            columns: table => new
            {
                Id = table.Column<long>(nullable: false)
                    .Annotation("Sqlite:Autoincrement", true)
                    .Annotation("SqlServer:Identity", "1, 1"),
                DeviceId = table.Column<string>(maxLength: 128, nullable: false),
                OrganizationId = table.Column<long>(nullable: false),
                FcmTokenProtected = table.Column<string>(nullable: false),
                FcmTokenHash = table.Column<string>(maxLength: 64, nullable: false),
                Platform = table.Column<string>(maxLength: 32, nullable: false),
                AppVersion = table.Column<string>(maxLength: 64, nullable: false),
                PushEnabled = table.Column<bool>(nullable: false),
                RegisteredAtUtc = table.Column<DateTime>(nullable: false),
                LastSeenAtUtc = table.Column<DateTime>(nullable: false),
                DeactivatedAtUtc = table.Column<DateTime>(nullable: true),
                ConcurrencyStamp = table.Column<string>(maxLength: 36, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MobileDevices", x => x.Id);
                table.ForeignKey(
                    name: "FK_MobileDevices_Organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "Organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "MobileDeliveries",
            columns: table => new
            {
                Id = table.Column<long>(nullable: false)
                    .Annotation("Sqlite:Autoincrement", true)
                    .Annotation("SqlServer:Identity", "1, 1"),
                OrganizationId = table.Column<long>(nullable: false),
                SecurePageId = table.Column<long>(nullable: false),
                CreatedAtUtc = table.Column<DateTime>(nullable: false),
                SentAtUtc = table.Column<DateTime>(nullable: true),
                DeliveryStatus = table.Column<string>(maxLength: 40, nullable: false),
                FirebaseStatus = table.Column<string>(maxLength: 40, nullable: true),
                FirebaseProviderMessageId = table.Column<string>(maxLength: 200, nullable: true),
                ExpiresAtUtc = table.Column<DateTime>(nullable: true),
                FirstRevealedAtUtc = table.Column<DateTime>(nullable: true),
                RevokedAtUtc = table.Column<DateTime>(nullable: true),
                ReminderEnabled = table.Column<bool>(nullable: false),
                ReminderInterval = table.Column<int>(nullable: true),
                ReminderUnit = table.Column<string>(maxLength: 20, nullable: true),
                NextReminderAtUtc = table.Column<DateTime>(nullable: true),
                LastReminderAtUtc = table.Column<DateTime>(nullable: true),
                ReminderCount = table.Column<int>(nullable: false),
                ConcurrencyStamp = table.Column<string>(maxLength: 36, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MobileDeliveries", x => x.Id);
                table.ForeignKey(
                    name: "FK_MobileDeliveries_Organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "Organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MobileDeliveries_SecurePages_SecurePageId",
                    column: x => x.SecurePageId,
                    principalTable: "SecurePages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "MobileRevealGrants",
            columns: table => new
            {
                Id = table.Column<long>(nullable: false)
                    .Annotation("Sqlite:Autoincrement", true)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TokenHash = table.Column<string>(maxLength: 64, nullable: false),
                MobileSessionId = table.Column<long>(nullable: false),
                MobileDeliveryId = table.Column<long>(nullable: false),
                CreatedAtUtc = table.Column<DateTime>(nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(nullable: false),
                ConsumedAtUtc = table.Column<DateTime>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MobileRevealGrants", x => x.Id);
                table.ForeignKey(
                    name: "FK_MobileRevealGrants_MobileDeliveries_MobileDeliveryId",
                    column: x => x.MobileDeliveryId,
                    principalTable: "MobileDeliveries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_MobileRevealGrants_MobileSessions_MobileSessionId",
                    column: x => x.MobileSessionId,
                    principalTable: "MobileSessions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_MobileOtpChallenges_ChallengeId", table: "MobileOtpChallenges", column: "ChallengeId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_MobileOtpChallenges_MobileNumber_CreatedAtUtc", table: "MobileOtpChallenges", columns: new[] { "MobileNumber", "CreatedAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_MobileOtpChallenges_OrganizationId", table: "MobileOtpChallenges", column: "OrganizationId");

        migrationBuilder.CreateIndex(name: "IX_MobileSessions_AccessTokenHash", table: "MobileSessions", column: "AccessTokenHash", unique: true);
        migrationBuilder.CreateIndex(name: "IX_MobileSessions_RefreshTokenHash", table: "MobileSessions", column: "RefreshTokenHash", unique: true);
        migrationBuilder.CreateIndex(name: "IX_MobileSessions_SessionId", table: "MobileSessions", column: "SessionId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_MobileSessions_OrganizationId_RefreshExpiresAtUtc", table: "MobileSessions", columns: new[] { "OrganizationId", "RefreshExpiresAtUtc" });

        migrationBuilder.CreateIndex(name: "IX_MobileDevices_DeviceId", table: "MobileDevices", column: "DeviceId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_MobileDevices_FcmTokenHash", table: "MobileDevices", column: "FcmTokenHash", unique: true);
        migrationBuilder.CreateIndex(name: "IX_MobileDevices_OrganizationId_DeactivatedAtUtc", table: "MobileDevices", columns: new[] { "OrganizationId", "DeactivatedAtUtc" });

        migrationBuilder.CreateIndex(name: "IX_MobileDeliveries_OrganizationId_CreatedAtUtc", table: "MobileDeliveries", columns: new[] { "OrganizationId", "CreatedAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_MobileDeliveries_SecurePageId_CreatedAtUtc", table: "MobileDeliveries", columns: new[] { "SecurePageId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(name: "IX_MobileRevealGrants_TokenHash", table: "MobileRevealGrants", column: "TokenHash", unique: true);
        migrationBuilder.CreateIndex(name: "IX_MobileRevealGrants_MobileDeliveryId", table: "MobileRevealGrants", column: "MobileDeliveryId");
        migrationBuilder.CreateIndex(name: "IX_MobileRevealGrants_MobileSessionId_MobileDeliveryId_ExpiresAtUtc", table: "MobileRevealGrants", columns: new[] { "MobileSessionId", "MobileDeliveryId", "ExpiresAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MobileRevealGrants");
        migrationBuilder.DropTable(name: "MobileDevices");
        migrationBuilder.DropTable(name: "MobileOtpChallenges");
        migrationBuilder.DropTable(name: "MobileDeliveries");
        migrationBuilder.DropTable(name: "MobileSessions");

        if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Organizations_MobileNumber\";");
        else if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Organizations_MobileNumber] ON [Organizations];");
        else
            throw new NotSupportedException($"Unsupported database provider {ActiveProvider}");

        migrationBuilder.DropColumn(name: "MobileNumber", table: "Organizations");
    }
}
