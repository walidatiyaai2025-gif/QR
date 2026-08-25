namespace SecureQrPortal.Data;

public static class BackupBootstrap
{
    public static void ApplyPendingRestore(string contentRoot, IConfiguration configuration)
    {
        var sqliteRelative = configuration["SecureQrPortal:DefaultSqliteFile"] ?? "App_Data/SecureQrPortal.db";
        var dbPath = Path.Combine(contentRoot, sqliteRelative.Replace('/', Path.DirectorySeparatorChar));
        var pending = dbPath + ".pendingrestore";
        if (!File.Exists(pending)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var previous = dbPath + ".pre-restore-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".bak";
        if (File.Exists(dbPath)) File.Copy(dbPath, previous, overwrite: false);
        File.Move(pending, dbPath, overwrite: true);
    }
}
