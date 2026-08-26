using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;

namespace SecureQrPortal.Services;

public sealed class DemoDataService(ApplicationDbContext db, TokenService tokens, AdminIdentityService admin)
{
    public async Task<int> CreateAsync(int pages = 25, CancellationToken ct = default)
    {
        pages = Math.Clamp(pages, 5, 500);
        var adminId = admin.CurrentUserId;
        var orgs = new[] {
            new Organization { NameArabic="الديوان الأميري — بيانات تجريبية", NameEnglish="Al Diwan Al Amiri — Demo", LogoPath="/images/sample/diwan-logo.svg", IsDemo=true },
            new Organization { NameArabic="كونا — بيانات تجريبية", NameEnglish="KUNA — Demo", LogoPath="/images/sample/kuna-logo.svg", IsDemo=true },
            new Organization { NameArabic="جهة تجريبية", NameEnglish="Demo Organization", IsDemo=true }
        };
        db.Organizations.AddRange(orgs); await db.SaveChangesAsync(ct);
        var hasher = new PasswordHasher<PageCredential>();
        for (var i=1;i<=pages;i++)
        {
            var raw=tokens.GenerateToken();
            var p=new SecurePage { OrganizationId=orgs[(i-1)%orgs.Length].Id, QrReference=$"PENDING-{Guid.NewGuid():N}"[..32], PublicTokenHash=TokenService.HashToken(raw), ProtectedPublicToken=tokens.Protect(raw), CurrentTokenCreatedAtUtc=DateTime.UtcNow, TitleArabic=$"صفحة آمنة تجريبية {i}", TitleEnglish=$"Demo Secure Page {i}", ContentArabicHtml="<h2>محتوى تجريبي</h2><p>هذه بيانات توضيحية ويمكن حذفها من لوحة الإدارة.</p>", ContentEnglishHtml="<h2>Demo content</h2><p>This record is clearly marked as demo data and can be removed from Admin.</p>", IsActive=i%7!=0, ValidFromUtc=i%9==0?DateTime.UtcNow.AddDays(1):DateTime.UtcNow.AddDays(-10), ExpiresAtUtc=i%6==0?DateTime.UtcNow.AddDays(-1):DateTime.UtcNow.AddDays(30-i), AccessLimitMode=AccessLimitMode.MaximumSuccessfulAccesses, MaxAccessCount=100, CreatedByAdminId=adminId, LastModifiedByAdminId=adminId, IsDemo=true, CreatedAtUtc=DateTime.UtcNow.AddDays(-i), UpdatedAtUtc=DateTime.UtcNow };
            db.SecurePages.Add(p); await db.SaveChangesAsync(ct); p.QrReference=$"QR-{p.CreatedAtUtc.Year}-{p.Id:000000}";
            var c=new PageCredential { SecurePageId=p.Id, Username=$"demo{i:000}", UpdatedAtUtc=DateTime.UtcNow }; c.PasswordHash=hasher.HashPassword(c, "Demo!Pass123"); db.PageCredentials.Add(c);
        }
        await db.SaveChangesAsync(ct); return pages;
    }

    public async Task<int> DeleteAsync(CancellationToken ct = default)
    {
        var pageIds = await db.SecurePages.Where(x => x.IsDemo).Select(x => x.Id).ToListAsync(ct);
        if (pageIds.Count > 0)
        {
            var deliveries = await db.MobileDeliveries.Where(x => pageIds.Contains(x.SecurePageId)).ToListAsync(ct);
            if (deliveries.Count > 0)
            {
                db.MobileDeliveries.RemoveRange(deliveries);
                await db.SaveChangesAsync(ct);
            }
        }

        var pages = await db.SecurePages.Where(x => x.IsDemo).ToListAsync(ct);
        db.SecurePages.RemoveRange(pages);
        await db.SaveChangesAsync(ct);

        var orgs = await db.Organizations
            .Where(x => x.IsDemo &&
                        !x.SecurePages.Any() &&
                        !db.MobileOtpChallenges.Any(m => m.OrganizationId == x.Id) &&
                        !db.MobileSessions.Any(m => m.OrganizationId == x.Id) &&
                        !db.MobileDevices.Any(m => m.OrganizationId == x.Id) &&
                        !db.MobileDeliveries.Any(m => m.OrganizationId == x.Id))
            .ToListAsync(ct);
        db.Organizations.RemoveRange(orgs);
        await db.SaveChangesAsync(ct);
        return pages.Count;
    }
}
