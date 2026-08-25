using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SecureQrPortal.Data;

#nullable disable
namespace SecureQrPortal.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("202608260002_FirebasePushReminders")]
public sealed class FirebasePushReminders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "FirebaseErrorCode", table: "MobileDeliveries", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<int>(name: "ReminderSequence", table: "MobileDeliveries", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<DateTime>(name: "ReminderCycleStartedAtUtc", table: "MobileDeliveries", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "ReminderCycleCompletedAtUtc", table: "MobileDeliveries", nullable: true);
        migrationBuilder.AddColumn<string>(name: "ProcessingLeaseId", table: "MobileDeliveries", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "ProcessingLeaseUntilUtc", table: "MobileDeliveries", nullable: true);

        migrationBuilder.CreateTable(
            name: "MobilePushAttempts",
            columns: table => new
            {
                Id = table.Column<long>(nullable: false)
                    .Annotation("Sqlite:Autoincrement", true)
                    .Annotation("SqlServer:Identity", "1, 1"),
                MobileDeliveryId = table.Column<long>(nullable: false),
                MobileDeviceId = table.Column<long>(nullable: true),
                Kind = table.Column<string>(maxLength: 20, nullable: false),
                Sequence = table.Column<int>(nullable: false),
                RetryNumber = table.Column<int>(nullable: false),
                CorrelationKey = table.Column<string>(maxLength: 256, nullable: false),
                DeviceId = table.Column<string>(maxLength: 128, nullable: false),
                FcmTokenHash = table.Column<string>(maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(nullable: false),
                CompletedAtUtc = table.Column<DateTime>(nullable: true),
                Outcome = table.Column<string>(maxLength: 40, nullable: false),
                ProviderMessageId = table.Column<string>(maxLength: 200, nullable: true),
                ProviderErrorCode = table.Column<string>(maxLength: 128, nullable: true),
                PermanentFailure = table.Column<bool>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MobilePushAttempts", x => x.Id);
                table.ForeignKey(
                    name: "FK_MobilePushAttempts_MobileDeliveries_MobileDeliveryId",
                    column: x => x.MobileDeliveryId,
                    principalTable: "MobileDeliveries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MobileDeliveries_ReminderEnabled_NextReminderAtUtc_ProcessingLeaseUntilUtc",
            table: "MobileDeliveries",
            columns: new[] { "ReminderEnabled", "NextReminderAtUtc", "ProcessingLeaseUntilUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_MobilePushAttempts_CorrelationKey",
            table: "MobilePushAttempts",
            column: "CorrelationKey",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_MobilePushAttempts_MobileDeliveryId_Kind_Sequence",
            table: "MobilePushAttempts",
            columns: new[] { "MobileDeliveryId", "Kind", "Sequence" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MobilePushAttempts");
        migrationBuilder.DropIndex(
            name: "IX_MobileDeliveries_ReminderEnabled_NextReminderAtUtc_ProcessingLeaseUntilUtc",
            table: "MobileDeliveries");
        migrationBuilder.DropColumn(name: "FirebaseErrorCode", table: "MobileDeliveries");
        migrationBuilder.DropColumn(name: "ReminderSequence", table: "MobileDeliveries");
        migrationBuilder.DropColumn(name: "ReminderCycleStartedAtUtc", table: "MobileDeliveries");
        migrationBuilder.DropColumn(name: "ReminderCycleCompletedAtUtc", table: "MobileDeliveries");
        migrationBuilder.DropColumn(name: "ProcessingLeaseId", table: "MobileDeliveries");
        migrationBuilder.DropColumn(name: "ProcessingLeaseUntilUtc", table: "MobileDeliveries");
    }
}
