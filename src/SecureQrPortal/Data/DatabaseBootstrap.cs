using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace SecureQrPortal.Data;

public static class DatabaseBootstrap
{
    private const string Purpose = "SecureQrPortal.DatabaseSettings.v1";

    public static DatabaseRuntimeOptions Load(string contentRoot, IConfiguration configuration)
    {
        var appData = Path.Combine(contentRoot, "App_Data");
        var file = Path.Combine(appData, "database.settings.json");
        var defaultSqlite = configuration["SecureQrPortal:DefaultSqliteFile"] ?? "App_Data/SecureQrPortal.db";
        if (!File.Exists(file))
            return new DatabaseRuntimeOptions { Provider = "SQLite", SqliteFile = defaultSqlite };

        try
        {
            var dto = JsonSerializer.Deserialize<DatabaseSettingsFile>(File.ReadAllText(file));
            if (dto is null)
                throw new InvalidOperationException("Database settings are empty.");

            if (dto.Provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(dto.ProtectedConnectionString))
                    throw new InvalidOperationException("SQL Server configuration is incomplete.");

                var provider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(appData, "keys")), cfg => cfg.SetApplicationName("SecureQrPortal"));
                var protector = provider.CreateProtector(Purpose);
                var connection = protector.Unprotect(dto.ProtectedConnectionString);
                return new DatabaseRuntimeOptions { Provider = "SqlServer", SqlServerConnectionString = connection, SqliteFile = dto.SqliteFile ?? defaultSqlite };
            }

            return new DatabaseRuntimeOptions { Provider = "SQLite", SqliteFile = dto.SqliteFile ?? defaultSqlite };
        }
        catch
        {
            // Fail closed to the local SQLite provider; the admin page exposes the configuration error after login.
            return new DatabaseRuntimeOptions { Provider = "SQLite", SqliteFile = defaultSqlite };
        }
    }

    public static void ConfigureDbContext(DbContextOptionsBuilder options, DatabaseRuntimeOptions runtime, string contentRoot)
    {
        if (runtime.IsSqlServer)
        {
            options.UseSqlServer(runtime.SqlServerConnectionString, sql =>
            {
                sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null);
                sql.CommandTimeout(30);
            });
        }
        else
        {
            var sqlitePath = Path.IsPathRooted(runtime.SqliteFile)
                ? runtime.SqliteFile
                : Path.Combine(contentRoot, runtime.SqliteFile.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(sqlitePath)!);
            options.UseSqlite($"Data Source={sqlitePath};Cache=Shared;Pooling=True");
        }
    }

    public sealed class DatabaseSettingsFile
    {
        public string Provider { get; set; } = "SQLite";
        public string? SqliteFile { get; set; }
        public string? ProtectedConnectionString { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public static string DataProtectionPurpose => Purpose;
}
