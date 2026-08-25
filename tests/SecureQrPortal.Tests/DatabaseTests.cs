using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;

namespace SecureQrPortal.Tests;

public sealed class DatabaseTests
{
    [Fact]
    public async Task Initial_migration_starts_on_local_sqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.MigrateAsync();
        Assert.Contains("202608250001_InitialCreate", await db.Database.GetAppliedMigrationsAsync());
        db.Organizations.Add(new Organization { NameArabic = "اختبار", NameEnglish = "Test" });
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.Organizations.CountAsync());
    }

    [Fact]
    public void Sql_server_2022_provider_configuration_is_parsed_without_connecting()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        DatabaseBootstrap.ConfigureDbContext(options, new DatabaseRuntimeOptions
        {
            Provider = "SqlServer",
            SqlServerConnectionString = "Server=localhost;Database=SecureQrPortal;Integrated Security=true;Encrypt=true;TrustServerCertificate=true"
        }, Path.GetTempPath());
        using var db = new ApplicationDbContext(options.Options);
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", db.Database.ProviderName);
    }
}
