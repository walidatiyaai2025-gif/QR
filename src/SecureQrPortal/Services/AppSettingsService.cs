using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;

namespace SecureQrPortal.Services;

public sealed class AppSettingsService(ApplicationDbContext db)
{
    public async Task<Dictionary<string,string>> GetAllAsync(CancellationToken ct = default) =>
        await db.ApplicationSettings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value, ct);

    public async Task<string> GetAsync(string key, string fallback = "", CancellationToken ct = default) =>
        await db.ApplicationSettings.AsNoTracking().Where(x => x.Key == key).Select(x => x.Value).SingleOrDefaultAsync(ct) ?? fallback;

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        var row = await db.ApplicationSettings.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (row is null) db.ApplicationSettings.Add(new ApplicationSetting { Key = key, Value = value, UpdatedAtUtc = DateTime.UtcNow });
        else { row.Value = value; row.UpdatedAtUtc = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct);
    }
}
