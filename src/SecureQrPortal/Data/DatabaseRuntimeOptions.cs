namespace SecureQrPortal.Data;

public sealed class DatabaseRuntimeOptions
{
    public string Provider { get; init; } = "SQLite";
    public string SqliteFile { get; init; } = "App_Data/SecureQrPortal.db";
    public string? SqlServerConnectionString { get; init; }
    public bool IsSqlServer => Provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
}
