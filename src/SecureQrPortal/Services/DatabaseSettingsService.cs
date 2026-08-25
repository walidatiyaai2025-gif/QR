using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;

namespace SecureQrPortal.Services;

public sealed class DatabaseSettingsService(IWebHostEnvironment env, IDataProtectionProvider dp, IConfiguration configuration)
{
    private readonly string _file = Path.Combine(env.ContentRootPath, "App_Data", "database.settings.json");
    private readonly IDataProtector _protector = dp.CreateProtector(DatabaseBootstrap.DataProtectionPurpose);

    public DatabaseRuntimeOptions Current => DatabaseBootstrap.Load(env.ContentRootPath, configuration);

    public string BuildSqlServerConnectionString(string server, string database, string authMode, string? username, string? password, bool encrypt, bool trust, int timeout)
    {
        var b = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource = server.Trim(), InitialCatalog = database.Trim(), Encrypt = encrypt,
            TrustServerCertificate = trust, ConnectTimeout = Math.Clamp(timeout, 3, 120),
            MultipleActiveResultSets = true, ApplicationName = "SecureQrPortal"
        };
        if (authMode.Equals("Windows", StringComparison.OrdinalIgnoreCase)) b.IntegratedSecurity = true;
        else { b.UserID = username?.Trim(); b.Password = password ?? string.Empty; }
        return b.ConnectionString;
    }

    public async Task<(bool ok,string message)> TestSqlServerAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connectionString, s => s.CommandTimeout(10)).Options;
            await using var probe = new ApplicationDbContext(options);
            var ok = await probe.Database.CanConnectAsync(ct);
            return ok ? (true, "Connection successful.") : (false, "Connection could not be established.");
        }
        catch (Exception ex) { return (false, ex.GetBaseException().Message); }
    }

    public async Task SaveSqlServerAsync(string connectionString, string updatedBy, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var dto = new DatabaseBootstrap.DatabaseSettingsFile
        {
            Provider = "SqlServer", SqliteFile = configuration["SecureQrPortal:DefaultSqliteFile"] ?? "App_Data/SecureQrPortal.db",
            ProtectedConnectionString = _protector.Protect(connectionString), UpdatedAtUtc = DateTime.UtcNow, UpdatedBy = updatedBy
        };
        await File.WriteAllTextAsync(_file, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }), ct);
    }

    public async Task SaveSqliteAsync(string updatedBy, CancellationToken ct = default)
    {
        var dto = new DatabaseBootstrap.DatabaseSettingsFile { Provider = "SQLite", SqliteFile = configuration["SecureQrPortal:DefaultSqliteFile"] ?? "App_Data/SecureQrPortal.db", UpdatedAtUtc = DateTime.UtcNow, UpdatedBy = updatedBy };
        await File.WriteAllTextAsync(_file, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }), ct);
    }

    public async Task InitializeSqlServerAsync(string connectionString, CancellationToken ct = default)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connectionString).Options;
        await using var target = new ApplicationDbContext(options);
        await target.Database.MigrateAsync(ct);
    }
}
