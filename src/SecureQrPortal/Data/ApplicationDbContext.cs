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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Organization>().HasIndex(x => x.NameArabic);
        builder.Entity<Organization>().HasIndex(x => x.NameEnglish);
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
        builder.Entity<SecurePage>()
            .HasOne(x => x.Credential).WithOne(x => x.SecurePage)
            .HasForeignKey<PageCredential>(x => x.SecurePageId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SecurePage>()
            .HasMany(x => x.AccessLogs).WithOne(x => x.SecurePage)
            .HasForeignKey(x => x.SecurePageId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<SecurePage>()
            .HasMany(x => x.TokenHistory).WithOne(x => x.SecurePage)
            .HasForeignKey(x => x.SecurePageId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Organization>()
            .HasMany(x => x.SecurePages).WithOne(x => x.Organization)
            .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
