using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Services;

namespace SecureQrPortal.Tests;

public sealed class QrShareClockRegressionTests
{
    private static readonly DateTimeOffset KnownUtc = new(2026, 8, 26, 9, 0, 0, TimeSpan.Zero);
    private const string ExistingQrPassword = "ExistingQr#Clock2026";

    [Fact]
    public async Task Link_expiry_uses_utc_and_does_not_expire_early()
    {
        await using var fixture = await CreateFixtureAsync(KnownUtc, TimeZoneInfo.Utc, linkLifetimeHours: 1, sessionDurationMinutes: 15);

        Assert.Equal(KnownUtc.UtcDateTime, fixture.Share.CreatedAtUtc);
        Assert.Equal(KnownUtc.AddHours(1).UtcDateTime, fixture.Share.ExpiresAtUtc);
        Assert.Equal(DateTimeKind.Utc, fixture.Share.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, fixture.Share.ExpiresAtUtc.Kind);

        var immediate = await fixture.Service.RevealAsync(fixture.RawToken, "clock-immediate");
        Assert.NotNull(immediate);

        fixture.Clock.Advance(TimeSpan.FromMinutes(59));
        var atFiftyNine = await fixture.Service.RevealAsync(fixture.RawToken, "clock-plus-59");
        Assert.NotNull(atFiftyNine);

        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        var afterExpiry = await fixture.Service.RevealAsync(fixture.RawToken, "clock-after-expiry");
        Assert.Null(afterExpiry);

        var persisted = await fixture.Service.FindByTokenAsync(fixture.RawToken);
        Assert.NotNull(persisted);
        Assert.Equal("LINK_EXPIRED", QrShareTime.BlockReason(persisted!, QrShareTime.UtcNow(fixture.Clock)));
    }

    [Fact]
    public async Task Link_expiry_is_independent_of_server_local_timezone()
    {
        var kuwaitLikeZone = TimeZoneInfo.CreateCustomTimeZone(
            "QR Clock UTC+03",
            TimeSpan.FromHours(3),
            "QR Clock UTC+03",
            "QR Clock UTC+03");

        await using var utcFixture = await CreateFixtureAsync(KnownUtc, TimeZoneInfo.Utc, linkLifetimeHours: 1, sessionDurationMinutes: 15);
        await using var kuwaitFixture = await CreateFixtureAsync(KnownUtc, kuwaitLikeZone, linkLifetimeHours: 1, sessionDurationMinutes: 15);

        Assert.Equal(utcFixture.Share.CreatedAtUtc, kuwaitFixture.Share.CreatedAtUtc);
        Assert.Equal(utcFixture.Share.ExpiresAtUtc, kuwaitFixture.Share.ExpiresAtUtc);

        utcFixture.Clock.Advance(TimeSpan.FromMinutes(59));
        kuwaitFixture.Clock.Advance(TimeSpan.FromMinutes(59));

        Assert.NotNull(await utcFixture.Service.RevealAsync(utcFixture.RawToken, "utc-zone-request"));
        Assert.NotNull(await kuwaitFixture.Service.RevealAsync(kuwaitFixture.RawToken, "kuwait-zone-request"));
    }

    [Fact]
    public async Task Database_roundtrip_preserves_utc_instant_and_service_restores_utc_kind()
    {
        await using var fixture = await CreateFixtureAsync(KnownUtc, TimeZoneInfo.Utc, linkLifetimeHours: 1, sessionDurationMinutes: 30);
        var revealed = await fixture.Service.RevealAsync(fixture.RawToken, "roundtrip-reveal");
        Assert.NotNull(revealed);

        fixture.Db.ChangeTracker.Clear();
        var rawDatabaseValue = await fixture.Db.QrShareLinks.AsNoTracking().SingleAsync();
        Assert.NotEqual(DateTimeKind.Local, rawDatabaseValue.ExpiresAtUtc.Kind);
        Assert.NotNull(rawDatabaseValue.AccessWindowEndsAtUtc);
        Assert.NotEqual(DateTimeKind.Local, rawDatabaseValue.AccessWindowEndsAtUtc!.Value.Kind);

        var loaded = await fixture.Service.FindByTokenAsync(fixture.RawToken);
        Assert.NotNull(loaded);
        Assert.Equal(DateTimeKind.Utc, loaded!.ExpiresAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, loaded.AccessWindowEndsAtUtc!.Value.Kind);
        Assert.Equal(rawDatabaseValue.ExpiresAtUtc.Ticks, loaded.ExpiresAtUtc.Ticks);
        Assert.Equal(rawDatabaseValue.AccessWindowEndsAtUtc.Value.Ticks, loaded.AccessWindowEndsAtUtc.Value.Ticks);
    }

    [Fact]
    public async Task Access_window_survives_link_expiry_but_cannot_create_a_new_reveal_after_link_expiry()
    {
        await using var fixture = await CreateFixtureAsync(KnownUtc, TimeZoneInfo.Utc, linkLifetimeHours: 1, sessionDurationMinutes: 120);
        var revealed = await fixture.Service.RevealAsync(fixture.RawToken, "access-window-start");
        Assert.NotNull(revealed);
        Assert.Equal(KnownUtc.AddMinutes(120).UtcDateTime, revealed!.Share.AccessWindowEndsAtUtc);

        fixture.Clock.Advance(TimeSpan.FromMinutes(61));

        Assert.Null(await fixture.Service.RevealAsync(fixture.RawToken, "new-reveal-after-link-expiry"));
        var stillAuthorized = await fixture.Service.VerifyCredentialAsync(
            fixture.Page.Id,
            "recipient",
            ExistingQrPassword);
        Assert.True(stillAuthorized.Success);
        Assert.Equal(KnownUtc.AddMinutes(120).UtcDateTime, stillAuthorized.HardExpiresAtUtc);
        Assert.Equal(DateTimeKind.Utc, stillAuthorized.HardExpiresAtUtc!.Value.Kind);

        fixture.Clock.Advance(TimeSpan.FromMinutes(60));
        var expiredWindow = await fixture.Service.VerifyCredentialAsync(
            fixture.Page.Id,
            "recipient",
            ExistingQrPassword);
        Assert.False(expiredWindow.Success);
    }

    [Fact]
    public void Cookie_receipt_expiry_does_not_shift_unspecified_database_utc_through_local_time()
    {
        var intendedUtc = KnownUtc.AddMinutes(15).UtcDateTime;
        var materializedWithoutKind = DateTime.SpecifyKind(intendedUtc, DateTimeKind.Unspecified);

        var cookieExpiry = QrShareTime.ToCookieExpiry(materializedWithoutKind);

        Assert.Equal(TimeSpan.Zero, cookieExpiry.Offset);
        Assert.Equal(intendedUtc, cookieExpiry.UtcDateTime);
        Assert.Equal(DateTimeKind.Utc, cookieExpiry.UtcDateTime.Kind);
    }

    private static async Task<ShareFixture> CreateFixtureAsync(
        DateTimeOffset utcNow,
        TimeZoneInfo localTimeZone,
        int linkLifetimeHours,
        int sessionDurationMinutes)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var org = new Organization { NameArabic = "جهة", NameEnglish = "Org", IsActive = true };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var page = new SecurePage
        {
            OrganizationId = org.Id,
            Organization = org,
            QrReference = "QR-2026-CLOCK-" + Guid.NewGuid().ToString("N")[..8],
            PublicTokenHash = Convert.ToHexString(Guid.NewGuid().ToByteArray()).PadRight(64, 'A')[..64],
            ProtectedPublicToken = "protected",
            TitleArabic = "صفحة",
            TitleEnglish = "Page",
            IsActive = true,
            ValidFromUtc = utcNow.UtcDateTime.AddMinutes(-1),
            ExpiresAtUtc = utcNow.UtcDateTime.AddHours(4)
        };
        db.SecurePages.Add(page);
        await db.SaveChangesAsync();

        var credential = new PageCredential { SecurePageId = page.Id, Username = "recipient" };
        credential.PasswordHash = new PasswordHasher<PageCredential>().HashPassword(credential, ExistingQrPassword);
        db.PageCredentials.Add(credential);
        await db.SaveChangesAsync();
        page.Credential = credential;

        var keyDir = Path.Combine(Path.GetTempPath(), "qr-share-clock-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(keyDir);
        var provider = DataProtectionProvider.Create(new DirectoryInfo(keyDir));
        var clock = new ManualTimeProvider(utcNow, localTimeZone);
        var service = new QrShareService(db, provider, clock);
        var share = await service.CreateAsync(
            page,
            maxOpenCount: 10,
            linkLifetimeHours,
            sessionDurationMinutes,
            ExistingQrPassword,
            "Open {ShareUrl} for {QrReference}",
            "clock-test-admin");
        var rawToken = service.GetRawToken(share);

        return new ShareFixture(connection, db, service, clock, share, page, rawToken, keyDir);
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow, TimeZoneInfo localTimeZone) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override TimeZoneInfo LocalTimeZone => localTimeZone;

        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    }

    private sealed class ShareFixture(
        SqliteConnection connection,
        ApplicationDbContext db,
        QrShareService service,
        ManualTimeProvider clock,
        QrShareLink share,
        SecurePage page,
        string rawToken,
        string keyDir) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;
        public QrShareService Service { get; } = service;
        public ManualTimeProvider Clock { get; } = clock;
        public QrShareLink Share { get; } = share;
        public SecurePage Page { get; } = page;
        public string RawToken { get; } = rawToken;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
            if (Directory.Exists(keyDir)) Directory.Delete(keyDir, true);
        }
    }
}
