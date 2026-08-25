using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SecureQrPortal.Data;

namespace SecureQrPortal.Tests;

public sealed class MobileMigrationTests
{
    [Fact]
    public async Task Sqlite_migration_chain_creates_mobile_auth_schema()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

        await db.Database.MigrateAsync();

        Assert.Equal(1L, await ScalarLongAsync(connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='MobileOtpChallenges';"));
        Assert.Equal(1L, await ScalarLongAsync(connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='MobileSessions';"));
        Assert.Equal(1L, await ScalarLongAsync(connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='MobileDevices';"));
        Assert.Equal(1L, await ScalarLongAsync(connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='MobileDeliveries';"));
        Assert.Equal(1L, await ScalarLongAsync(connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='MobileRevealGrants';"));
        Assert.Equal(1L, await ScalarLongAsync(connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='MobilePushAttempts';"));
        Assert.Equal(1L, await ScalarLongAsync(connection,
            "SELECT COUNT(*) FROM pragma_table_info('Organizations') WHERE name='MobileNumber';"));
        Assert.Equal(1L, await ScalarLongAsync(connection,
            "SELECT COUNT(*) FROM pragma_table_info('MobileDeliveries') WHERE name='ProcessingLeaseId';"));
        Assert.Equal(1L, await ScalarLongAsync(connection,
            "SELECT COUNT(*) FROM pragma_table_info('MobileDeliveries') WHERE name='ProcessingLeaseUntilUtc';"));
        Assert.Equal(1L, await ScalarLongAsync(connection,
            "SELECT COUNT(*) FROM pragma_table_info('MobileDeliveries') WHERE name='ReminderSequence';"));

        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText = "SELECT sql FROM sqlite_master WHERE type='index' AND name='IX_Organizations_MobileNumber';";
        var indexSql = (string?)await indexCommand.ExecuteScalarAsync();
        Assert.NotNull(indexSql);
        Assert.Contains("WHERE \"MobileNumber\" IS NOT NULL", indexSql!, StringComparison.OrdinalIgnoreCase);

        await using var attemptIndexCommand = connection.CreateCommand();
        attemptIndexCommand.CommandText = "SELECT sql FROM sqlite_master WHERE type='index' AND name='IX_MobilePushAttempts_CorrelationKey';";
        var attemptIndexSql = (string?)await attemptIndexCommand.ExecuteScalarAsync();
        Assert.NotNull(attemptIndexSql);
        Assert.Contains("UNIQUE", attemptIndexSql!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqlServer_provider_can_generate_full_mobile_migration_script_without_connecting()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=DaSecureMigrationScriptOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new ApplicationDbContext(options);
        var script = db.GetService<IMigrator>().GenerateScript();

        Assert.Contains("CREATE TABLE [MobileOtpChallenges]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE [MobileSessions]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE [MobileDevices]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE [MobileDeliveries]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE [MobileRevealGrants]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE [MobilePushAttempts]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[ProcessingLeaseId]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[ProcessingLeaseUntilUtc]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[ReminderSequence]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE UNIQUE INDEX [IX_MobilePushAttempts_CorrelationKey]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE UNIQUE INDEX [IX_Organizations_MobileNumber]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE [MobileNumber] IS NOT NULL", script, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
