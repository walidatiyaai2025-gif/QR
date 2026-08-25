using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Models;

namespace SecureQrPortal.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<SecurePage> SecurePages => Set<SecurePage>();
    public DbSet<PageCredential> PageCredentials => Set<PageCredential>();
    public DbSet<AccessLog> AccessLogs => Set<AccessLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ApplicationSetting> ApplicationSettings => Set<ApplicationSetting>();
    public DbSet<QrTokenHistory> QrTokenHistories => Set<QrTokenHistory>();
    public DbSet<QrShareLink> QrShareLinks => Set<QrShareLink>();
    public DbSet<MobileOtpChallenge> MobileOtpChallenges => Set<MobileOtpChallenge>();
    public DbSet<MobileSession> MobileSessions => Set<MobileSession>();
    public DbSet<MobileDevice> MobileDevices => Set<MobileDevice>();
    public DbSet<MobileDelivery> MobileDeliveries => Set<MobileDelivery>();
    public DbSet<MobileRevealGrant> MobileRevealGrants => Set<MobileRevealGrant>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Organization>().HasIndex(x => x.NameArabic);
        builder.Entity<Organization>().HasIndex(x => x.NameEnglish);
        builder.Entity<Organization>().HasIndex(x => x.MobileNumber).IsUnique();
        builder.Entity<SecurePage>().HasIndex(x => x.QrReference).IsUnique();
        builder.Entity<SecurePage>().HasIndex(x => x.PublicTokenHash).IsUnique();
        builder.Entity<SecurePage>().HasIndex(x => new { x.OrganizationId, x.CreatedAtUtc });
        builder.Entity<SecurePage>().Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        builder.Entity<PageCredential>().HasIndex(x => x.SecurePageId).IsUnique();
        builder.Entity<PageCredential>().HasIndex(x => new { x.SecurePageId, x.Username }).IsUnique();
        builder.Entity<AccessLog>().HasIndex(x => new { x.SecurePageId, x.TimestampUtc });
        builder.Entity<AccessLog>().HasIndex(x => new { x.EventType, x.TimestampUtc });
        builder.Entity<AuditLog>().HasIndex(x => x.TimestampUtc);
        builder.Entity<ApplicationSetting>().HasIndex(x => x.Key).IsUnique();
        builder.Entity<QrTokenHistory>().HasIndex(x => new { x.SecurePageId, x.RevokedAtUtc });
        builder.Entity<QrShareLink>().HasIndex(x => x.TokenHash).IsUnique();
        builder.Entity<QrShareLink>().HasIndex(x => new { x.SecurePageId, x.CreatedAtUtc });

        builder.Entity<MobileOtpChallenge>().HasIndex(x => x.ChallengeId).IsUnique();
        builder.Entity<MobileOtpChallenge>().HasIndex(x => new { x.MobileNumber, x.CreatedAtUtc });
        builder.Entity<MobileSession>().HasIndex(x => x.SessionId).IsUnique();
        builder.Entity<MobileSession>().HasIndex(x => x.AccessTokenHash).IsUnique();
        builder.Entity<MobileSession>().HasIndex(x => x.RefreshTokenHash).IsUnique();
        builder.Entity<MobileSession>().HasIndex(x => new { x.OrganizationId, x.RefreshExpiresAtUtc });
        builder.Entity<MobileDevice>().HasIndex(x => x.DeviceId).IsUnique();
        builder.Entity<MobileDevice>().HasIndex(x => x.FcmTokenHash).IsUnique();
        builder.Entity<MobileDevice>().HasIndex(x => new { x.OrganizationId, x.DeactivatedAtUtc });
        builder.Entity<MobileDevice>().Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        builder.Entity<MobileDelivery>().HasIndex(x => new { x.OrganizationId, x.CreatedAtUtc });
        builder.Entity<MobileDelivery>().HasIndex(x => new { x.SecurePageId, x.CreatedAtUtc });
        builder.Entity<MobileDelivery>().Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        builder.Entity<MobileRevealGrant>().HasIndex(x => x.TokenHash).IsUnique();
        builder.Entity<MobileRevealGrant>().HasIndex(x => new { x.MobileSessionId, x.MobileDeliveryId, x.ExpiresAtUtc });

        builder.Entity<SecurePage>()
            .HasOne(x => x.Credential).WithOne(x => x.SecurePage)
            .HasForeignKey<PageCredential>(x => x.SecurePageId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SecurePage>()
            .HasMany(x => x.AccessLogs).WithOne(x => x.SecurePage)
            .HasForeignKey(x => x.SecurePageId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<SecurePage>()
            .HasMany(x => x.TokenHistory).WithOne(x => x.SecurePage)
            .HasForeignKey(x => x.SecurePageId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SecurePage>()
            .HasMany(x => x.ShareLinks).WithOne(x => x.SecurePage)
            .HasForeignKey(x => x.SecurePageId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Organization>()
            .HasMany(x => x.SecurePages).WithOne(x => x.Organization)
            .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MobileOtpChallenge>()
            .HasOne(x => x.Organization).WithMany()
            .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<MobileSession>()
            .HasOne(x => x.Organization).WithMany()
            .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<MobileDevice>()
            .HasOne(x => x.Organization).WithMany()
            .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<MobileDelivery>()
            .HasOne(x => x.Organization).WithMany()
            .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<MobileDelivery>()
            .HasOne(x => x.SecurePage).WithMany()
            .HasForeignKey(x => x.SecurePageId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<MobileRevealGrant>()
            .HasOne(x => x.MobileSession).WithMany()
            .HasForeignKey(x => x.MobileSessionId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<MobileRevealGrant>()
            .HasOne(x => x.MobileDelivery).WithMany()
            .HasForeignKey(x => x.MobileDeliveryId).OnDelete(DeleteBehavior.Cascade);
    }
}
