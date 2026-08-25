using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;

namespace SecureQrPortal.Services;

public sealed class BackupService(ApplicationDbContext db, IWebHostEnvironment env, IConfiguration config)
{
    private string DbPath => Path.Combine(env.ContentRootPath, (config["SecureQrPortal:DefaultSqliteFile"] ?? "App_Data/SecureQrPortal.db").Replace('/', Path.DirectorySeparatorChar));
    private string BackupDir => Path.Combine(env.ContentRootPath, "App_Data", "backups");

    public async Task<string> CreateLocalBackupAsync(CancellationToken ct = default)
    {
        if (!db.Database.IsSqlite()) throw new InvalidOperationException("Local file backup is available only in SQLite mode.");
        Directory.CreateDirectory(BackupDir);
        var path = Path.Combine(BackupDir, $"SecureQrPortal-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");

        var source = (SqliteConnection)db.Database.GetDbConnection();
        var wasClosed = source.State == System.Data.ConnectionState.Closed;
        if (wasClosed) await source.OpenAsync(ct);
        try
        {
            await using var target = new SqliteConnection($"Data Source={path}");
            await target.OpenAsync(ct);
            source.BackupDatabase(target);
        }
        finally
        {
            if (wasClosed) await source.CloseAsync();
        }

        await VerifySqliteBackupAsync(path, ct);
        return path;
    }

    public IEnumerable<FileInfo> History() => Directory.Exists(BackupDir)
        ? new DirectoryInfo(BackupDir).GetFiles("*.db").OrderByDescending(x => x.CreationTimeUtc).Take(30)
        : Enumerable.Empty<FileInfo>();

    public async Task StageRestoreAsync(Stream source, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlite()) throw new InvalidOperationException("SQLite restore is available only in SQLite mode.");
        var pending = DbPath + ".pendingrestore";
        Directory.CreateDirectory(Path.GetDirectoryName(pending)!);
        try
        {
            await using (var fs = File.Create(pending))
            {
                await source.CopyToAsync(fs, ct);
                await fs.FlushAsync(ct);
            }
            await VerifySqliteBackupAsync(pending, ct);
        }
        catch
        {
            if (File.Exists(pending)) File.Delete(pending);
            throw;
        }
    }

    private static async Task VerifySqliteBackupAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < 100) throw new InvalidOperationException("SQLite backup is empty or invalid.");
        await using var check = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await check.OpenAsync(ct);
        await using (var integrity = check.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            var result = Convert.ToString(await integrity.ExecuteScalarAsync(ct));
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("SQLite integrity verification failed.");
        }
        await using var schema = check.CreateCommand();
        schema.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SecurePages';";
        if (Convert.ToInt64(await schema.ExecuteScalarAsync(ct)) != 1) throw new InvalidOperationException("The uploaded database is not a Secure QR Portal backup.");
    }
}
