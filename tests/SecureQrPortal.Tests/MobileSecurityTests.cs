using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SecureQrPortal.Controllers;
using SecureQrPortal.Data;
using SecureQrPortal.Models;
using SecureQrPortal.Security;
using SecureQrPortal.Services;

namespace SecureQrPortal.Tests;

public sealed class MobileSecurityTests
{
    [Theory]
    [InlineData("50000000", "96550000000")]
    [InlineData("+965 5000-0000", "96550000000")]
    [InlineData("0096550000000", "96550000000")]
    public void Kuwait_mobile_normalization_is_canonical(string input, string expected) =>
        Assert.Equal(expected, MobileNumberNormalizer.NormalizeKuwait(input));

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("96650000000")]
    [InlineData("965500000000")]
    public void Malformed_or_non_Kuwait_mobile_is_rejected(string input) =>
        Assert.Null(MobileNumberNormalizer.NormalizeKuwait(input));

    [Fact]
    public async Task Request_otp_does_not_expose_registration_or_otp()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedOrganizationAsync("96550000001");

        var registered = await f.Otp.RequestAsync("96550000001");
        var unknown = await f.Otp.RequestAsync("96550000002");

        Assert.Equal(MobileOtpRequestStatus.Accepted, registered.Status);
        Assert.Equal(MobileOtpRequestStatus.Accepted, unknown.Status);
        Assert.NotNull(registered.ChallengeId);
        Assert.NotNull(unknown.ChallengeId);
        Assert.DoesNotContain(typeof(MobileOtpRequestOutcome).GetProperties(), p =>
            string.Equals(p.Name, "Otp", StringComparison.OrdinalIgnoreCase));

        var persisted = await f.Db.MobileOtpChallenges.AsNoTracking().SingleAsync();
        Assert.True(persisted.RevokedAtUtc.HasValue); // Disabled test gateway fails closed.
        Assert.False(persisted.ProviderSucceeded);
    }

    [Fact]
    public async Task Otp_is_hmac_protected_single_use_and_never_audited_in_plaintext()
    {
        await using var f = await Fixture.CreateAsync();
        var org = await f.SeedOrganizationAsync("96550000003");
        var challengeId = f.Tokens.GenerateToken(24);
        var material = f.Secrets.CreateOtp(challengeId);
        f.Db.MobileOtpChallenges.Add(new MobileOtpChallenge
        {
            ChallengeId = challengeId,
            OrganizationId = org.Id,
            MobileNumber = org.MobileNumber!,
            OtpHash = material.OtpHash,
            ProtectedVerificationKey = material.ProtectedVerificationKey,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            ResendAvailableAtUtc = DateTime.UtcNow.AddSeconds(60),
            MaxAttempts = 5,
            ProviderSucceeded = true
        });
        await f.Db.SaveChangesAsync();

        Assert.NotEqual(material.Otp, material.OtpHash);
        var success = await f.Otp.VerifyAsync(challengeId, material.Otp);
        Assert.Equal(MobileOtpVerifyStatus.Success, success.Status);
        Assert.NotNull(success.Session);

        var replay = await f.Otp.VerifyAsync(challengeId, material.Otp);
        Assert.Equal(MobileOtpVerifyStatus.Invalid, replay.Status);
        var auditText = string.Join("\n", await f.Db.AuditLogs.AsNoTracking().Select(x => x.Details ?? string.Empty).ToListAsync());
        Assert.DoesNotContain(material.Otp, auditText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Otp_expiry_is_enforced()
    {
        await using var f = await Fixture.CreateAsync();
        var org = await f.SeedOrganizationAsync("96550000004");
        var challengeId = f.Tokens.GenerateToken(24);
        var material = f.Secrets.CreateOtp(challengeId);
        f.Db.MobileOtpChallenges.Add(new MobileOtpChallenge
        {
            ChallengeId = challengeId,
            OrganizationId = org.Id,
            MobileNumber = org.MobileNumber!,
            OtpHash = material.OtpHash,
            ProtectedVerificationKey = material.ProtectedVerificationKey,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1),
            ResendAvailableAtUtc = DateTime.UtcNow.AddMinutes(-9),
            MaxAttempts = 5,
            ProviderSucceeded = true
        });
        await f.Db.SaveChangesAsync();

        var result = await f.Otp.VerifyAsync(challengeId, material.Otp);
        Assert.Equal(MobileOtpVerifyStatus.Expired, result.Status);
        Assert.Empty(f.Db.MobileSessions);
    }

    [Fact]
    public async Task Otp_attempt_limit_is_enforced()
    {
        await using var f = await Fixture.CreateAsync();
        var org = await f.SeedOrganizationAsync("96550000005");
        var challengeId = f.Tokens.GenerateToken(24);
        var material = f.Secrets.CreateOtp(challengeId);
        f.Db.MobileOtpChallenges.Add(new MobileOtpChallenge
        {
            ChallengeId = challengeId,
            OrganizationId = org.Id,
            MobileNumber = org.MobileNumber!,
            OtpHash = material.OtpHash,
            ProtectedVerificationKey = material.ProtectedVerificationKey,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            ResendAvailableAtUtc = DateTime.UtcNow.AddSeconds(60),
            MaxAttempts = 2,
            ProviderSucceeded = true
        });
        await f.Db.SaveChangesAsync();

        Assert.Equal(MobileOtpVerifyStatus.Invalid, (await f.Otp.VerifyAsync(challengeId, "000000")).Status);
        Assert.Equal(MobileOtpVerifyStatus.TooManyAttempts, (await f.Otp.VerifyAsync(challengeId, "111111")).Status);
        Assert.Equal(MobileOtpVerifyStatus.TooManyAttempts, (await f.Otp.VerifyAsync(challengeId, material.Otp)).Status);
        Assert.Empty(f.Db.MobileSessions);
    }

    [Fact]
    public void Otp_resend_cooldown_is_enforced_by_mobile_dimension()
    {
        var throttle = new MobileOtpThrottle(TimeProvider.System);
        Assert.Equal(MobileOtpRequestStatus.Accepted, throttle.TryAcquire("96550000006", out _));
        Assert.Equal(MobileOtpRequestStatus.Cooldown, throttle.TryAcquire("96550000006", out var retry));
        Assert.True(retry > 0);
    }

    [Fact]
    public async Task Refresh_token_rotates_and_old_refresh_cannot_be_replayed()
    {
        await using var f = await Fixture.CreateAsync();
        var org = await f.SeedOrganizationAsync("96550000007");
        var first = await f.Sessions.IssueAsync(org);
        var second = await f.Sessions.RefreshAsync(first.RefreshToken);

        Assert.NotNull(second);
        Assert.NotEqual(first.AccessToken, second!.AccessToken);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.Null(await f.Sessions.RefreshAsync(first.RefreshToken));
        Assert.NotNull(await f.Sessions.RefreshAsync(second.RefreshToken));
    }

    [Fact]
    public async Task Raw_access_and_refresh_tokens_are_not_persisted()
    {
        await using var f = await Fixture.CreateAsync();
        var org = await f.SeedOrganizationAsync("96550000008");
        var issued = await f.Sessions.IssueAsync(org);
        var row = await f.Db.MobileSessions.AsNoTracking().SingleAsync();

        Assert.NotEqual(issued.AccessToken, row.AccessTokenHash);
        Assert.NotEqual(issued.RefreshToken, row.RefreshTokenHash);
        Assert.Equal(f.Tokens.HashToken(issued.AccessToken), row.AccessTokenHash);
        Assert.Equal(f.Tokens.HashToken(issued.RefreshToken), row.RefreshTokenHash);
    }

    [Fact]
    public async Task Device_registration_cannot_reassign_device_to_another_organization()
    {
        await using var f = await Fixture.CreateAsync();
        var orgA = await f.SeedOrganizationAsync("96550000009");
        var orgB = await f.SeedOrganizationAsync("96550000010");
        var first = await f.Devices.RegisterAsync(orgA.Id, "device-1", "fcm-token-1", "android", "1.0.0", true);
        var attack = await f.Devices.RegisterAsync(orgB.Id, "device-1", "fcm-token-2", "android", "1.0.0", true);

        Assert.Equal(MobileDeviceRegistrationStatus.Success, first.Status);
        Assert.Equal(MobileDeviceRegistrationStatus.Conflict, attack.Status);
        var stored = await f.Db.MobileDevices.AsNoTracking().SingleAsync();
        Assert.Equal(orgA.Id, stored.OrganizationId);
        Assert.DoesNotContain("fcm-token-1", stored.FcmTokenProtected, StringComparison.Ordinal);
    }

    [Fact]
    public void Mobile_request_contracts_do_not_accept_authoritative_organization_id()
    {
        var requestTypes = new[]
        {
            typeof(RequestOtpRequest), typeof(VerifyOtpRequest), typeof(RefreshMobileSessionRequest),
            typeof(RegisterMobileDeviceRequest), typeof(SecureMessageAuthenticateRequest), typeof(SecureMessageRevealRequest)
        };
        foreach (var type in requestTypes)
            Assert.DoesNotContain(type.GetProperties(), x => string.Equals(x.Name, "OrganizationId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Organization_A_cannot_fetch_or_reveal_organization_B_delivery()
    {
        await using var f = await Fixture.CreateAsync();
        var orgA = await f.SeedOrganizationAsync("96550000011");
        var orgB = await f.SeedOrganizationAsync("96550000012");
        var (_, deliveryB) = await f.SeedDeliveryAsync(orgB, 3);
        var sessionA = await f.Sessions.IssueAsync(orgA);
        var sessionRowA = await f.Db.MobileSessions.AsNoTracking().SingleAsync(x => x.SessionId == sessionA.SessionId);

        var details = await f.Deliveries.GetDetailsAsync(orgA.Id, deliveryB.Id);
        Assert.Equal(MobileDeliveryAccessStatus.NotFound, details.Status);
        Assert.Null(details.Details);

        var reveal = await f.Deliveries.RevealAsync(orgA.Id, sessionRowA.Id, deliveryB.Id,
            f.Tokens.GenerateToken(), f.Http);
        Assert.Equal(MobileDeliveryAccessStatus.NotFound, reveal.Status);
    }

    [Fact]
    public async Task Wrong_secure_credentials_do_not_consume_reveal()
    {
        await using var f = await Fixture.CreateAsync();
        var org = await f.SeedOrganizationAsync("96550000013");
        var (page, delivery) = await f.SeedDeliveryAsync(org, 2);
        var issued = await f.Sessions.IssueAsync(org);
        var session = await f.Db.MobileSessions.AsNoTracking().SingleAsync(x => x.SessionId == issued.SessionId);

        var result = await f.Deliveries.AuthenticateAsync(org.Id, session.Id, delivery.Id,
            "page-user", "wrong-password", f.Http);
        Assert.Equal(MobileDeliveryAccessStatus.InvalidCredentials, result.Status);
        var refreshed = await f.Db.SecurePages.AsNoTracking().SingleAsync(x => x.Id == page.Id);
        Assert.Equal(0, refreshed.CurrentSuccessfulAccessCount);
        Assert.Equal(1, refreshed.CurrentFailedLoginCount);
        Assert.Empty(f.Db.MobileRevealGrants);
    }

    [Fact]
    public async Task Secure_body_is_unavailable_in_inbox_and_details_before_authentication()
    {
        await using var f = await Fixture.CreateAsync();
        var org = await f.SeedOrganizationAsync("96550000014");
        var (_, delivery) = await f.SeedDeliveryAsync(org, 2);
        var inbox = await f.Deliveries.GetInboxAsync(org.Id, 1, 20);
        var details = await f.Deliveries.GetDetailsAsync(org.Id, delivery.Id);

        Assert.Single(inbox.Items);
        Assert.NotNull(details.Details);
        Assert.DoesNotContain(typeof(MobileInboxItem).GetProperties(), p => p.Name.Contains("Content", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(MobileDeliveryDetails).GetProperties(), p => p.Name.Contains("Content", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(MobileDeliveryDetails).GetProperties(), p => p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(MobileDeliveryDetails).GetProperties(), p => p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Successful_reveal_consumes_exactly_one_and_grant_is_single_use()
    {
        await using var f = await Fixture.CreateAsync();
        var org = await f.SeedOrganizationAsync("96550000015");
        var (page, delivery) = await f.SeedDeliveryAsync(org, 3);
        var issued = await f.Sessions.IssueAsync(org);
        var session = await f.Db.MobileSessions.AsNoTracking().SingleAsync(x => x.SessionId == issued.SessionId);
        var auth = await f.Deliveries.AuthenticateAsync(org.Id, session.Id, delivery.Id,
            "page-user", "Correct!Pass123", f.Http);
        Assert.Equal(MobileDeliveryAccessStatus.Success, auth.Status);

        var reveal = await f.Deliveries.RevealAsync(org.Id, session.Id, delivery.Id, auth.RevealToken, f.Http);
        Assert.Equal(MobileDeliveryAccessStatus.Success, reveal.Status);
        Assert.Equal("<p>Arabic secure body</p>", reveal.ContentArabicHtml);
        Assert.Equal("<p>English secure body</p>", reveal.ContentEnglishHtml);
        Assert.Equal(2, reveal.RemainingReveals);

        var row = await f.Db.SecurePages.AsNoTracking().SingleAsync(x => x.Id == page.Id);
        Assert.Equal(1, row.CurrentSuccessfulAccessCount);
        Assert.Equal(MobileDeliveryAccessStatus.InvalidRevealGrant,
            (await f.Deliveries.RevealAsync(org.Id, session.Id, delivery.Id, auth.RevealToken, f.Http)).Status);
        Assert.Equal(1, (await f.Db.SecurePages.AsNoTracking().SingleAsync(x => x.Id == page.Id)).CurrentSuccessfulAccessCount);
    }

    [Fact]
    public async Task Reveal_limit_cannot_be_overspent_with_two_valid_grants()
    {
        await using var f = await Fixture.CreateAsync();
        var org = await f.SeedOrganizationAsync("96550000016");
        var (page, delivery) = await f.SeedDeliveryAsync(org, 1);
        var issued = await f.Sessions.IssueAsync(org);
        var session = await f.Db.MobileSessions.AsNoTracking().SingleAsync(x => x.SessionId == issued.SessionId);
        var auth1 = await f.Deliveries.AuthenticateAsync(org.Id, session.Id, delivery.Id, "page-user", "Correct!Pass123", f.Http);
        var auth2 = await f.Deliveries.AuthenticateAsync(org.Id, session.Id, delivery.Id, "page-user", "Correct!Pass123", f.Http);

        Assert.Equal(MobileDeliveryAccessStatus.Success,
            (await f.Deliveries.RevealAsync(org.Id, session.Id, delivery.Id, auth1.RevealToken, f.Http)).Status);
        Assert.Equal(MobileDeliveryAccessStatus.LimitReached,
            (await f.Deliveries.RevealAsync(org.Id, session.Id, delivery.Id, auth2.RevealToken, f.Http)).Status);
        Assert.Equal(1, (await f.Db.SecurePages.AsNoTracking().SingleAsync(x => x.Id == page.Id)).CurrentSuccessfulAccessCount);
    }

    [Fact]
    public async Task Expired_and_revoked_delivery_are_denied_server_side()
    {
        await using var f = await Fixture.CreateAsync();
        var org = await f.SeedOrganizationAsync("96550000017");
        var (_, expired) = await f.SeedDeliveryAsync(org, 2, deliveryExpiresAtUtc: DateTime.UtcNow.AddMinutes(-1));
        var (_, revoked) = await f.SeedDeliveryAsync(org, 2, revokedAtUtc: DateTime.UtcNow);
        var issued = await f.Sessions.IssueAsync(org);
        var session = await f.Db.MobileSessions.AsNoTracking().SingleAsync(x => x.SessionId == issued.SessionId);

        Assert.Equal(MobileDeliveryAccessStatus.Expired,
            (await f.Deliveries.AuthenticateAsync(org.Id, session.Id, expired.Id, "page-user", "Correct!Pass123", f.Http)).Status);
        Assert.Equal(MobileDeliveryAccessStatus.Revoked,
            (await f.Deliveries.AuthenticateAsync(org.Id, session.Id, revoked.Id, "page-user", "Correct!Pass123", f.Http)).Status);
    }

    [Fact]
    public async Task Reveal_api_returns_zero_attachments_without_blocking_text_only_message()
    {
        await using var f = await Fixture.CreateAsync();
        var org = await f.SeedOrganizationAsync("96550000018");
        var (_, delivery) = await f.SeedDeliveryAsync(org, 2);
        var issued = await f.Sessions.IssueAsync(org);
        var session = await f.Db.MobileSessions.AsNoTracking().SingleAsync(x => x.SessionId == issued.SessionId);
        var auth = await f.Deliveries.AuthenticateAsync(org.Id, session.Id, delivery.Id, "page-user", "Correct!Pass123", f.Http);

        f.Http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(MobileClaimTypes.OrganizationId, org.Id.ToString()),
            new Claim(MobileClaimTypes.SessionDatabaseId, session.Id.ToString())
        }, MobileBearerDefaults.Scheme));
        var controller = new MobileInboxController(f.Deliveries)
        {
            ControllerContext = new ControllerContext { HttpContext = f.Http }
        };
        var action = await controller.Reveal(delivery.Id, new SecureMessageRevealRequest(auth.RevealToken), default);
        var ok = Assert.IsType<OkObjectResult>(action);
        var attachments = ok.Value!.GetType().GetProperty("attachments")!.GetValue(ok.Value);
        Assert.Empty(Assert.IsAssignableFrom<Array>(attachments));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public ApplicationDbContext Db { get; }
        public DefaultHttpContext Http { get; } = new();
        public MobileTokenService Tokens { get; } = new();
        public MobileSecretProtector Secrets { get; }
        public MobileSessionService Sessions { get; }
        public MobileOtpService Otp { get; }
        public MobileDeviceService Devices { get; }
        public MobileDeliveryAccessService Deliveries { get; }
        public SecureMessageEncryptionService Encryption { get; }

        private Fixture(SqliteConnection connection, ApplicationDbContext db)
        {
            this.connection = connection;
            Db = db;
            var time = TimeProvider.System;
            var dataProtection = new EphemeralDataProtectionProvider();
            var securitySettings = new SecureMessageSecuritySettingsService(new AppSettingsService(Db));
            Encryption = new SecureMessageEncryptionService(dataProtection, securitySettings);
            Secrets = new MobileSecretProtector(dataProtection);
            var accessor = new HttpContextAccessor { HttpContext = Http };
            var audit = new AuditService(Db, accessor);
            Sessions = new MobileSessionService(Db, Tokens, time);
            Otp = new MobileOtpService(Db, Secrets, Sessions, new MobileOtpThrottle(time),
                new SmsGatewayService(new ConfigurationBuilder().Build()), audit, Tokens, time);
            Devices = new MobileDeviceService(Db, Secrets, Tokens, audit, time);
            var pageAccess = new SecurePageAccessService(Db, null!, new QrStatusService(time), new DeviceInfoService());
            Deliveries = new MobileDeliveryAccessService(Db, pageAccess, new QrStatusService(time), Tokens, Encryption, audit, time);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            db.ApplicationSettings.AddRange(
                new ApplicationSetting { Key = SecureMessageSecuritySettingsService.EnabledKey, Value = "true" },
                new ApplicationSetting { Key = SecureMessageSecuritySettingsService.AllowRevealKey, Value = "true" });
            await db.SaveChangesAsync();
            return new Fixture(connection, db);
        }

        public async Task<Organization> SeedOrganizationAsync(string mobile)
        {
            var org = new Organization
            {
                NameArabic = "جهة " + mobile[^2..],
                NameEnglish = "Org " + mobile[^2..],
                MobileNumber = mobile,
                IsActive = true
            };
            Db.Organizations.Add(org);
            await Db.SaveChangesAsync();
            return org;
        }

        public async Task<(SecurePage Page, MobileDelivery Delivery)> SeedDeliveryAsync(
            Organization organization,
            long maxReveals,
            DateTime? deliveryExpiresAtUtc = null,
            DateTime? revokedAtUtc = null)
        {
            var page = new SecurePage
            {
                OrganizationId = organization.Id,
                Organization = organization,
                QrReference = "QR-2026-" + Guid.NewGuid().ToString("N")[..12],
                PublicTokenHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                ProtectedPublicToken = "protected-test-token",
                TitleArabic = "رسالة",
                TitleEnglish = "Message",
                ContentArabicHtml = "<p>Arabic secure body</p>",
                ContentEnglishHtml = "<p>English secure body</p>",
                IsActive = true,
                ValidFromUtc = DateTime.UtcNow.AddDays(-1),
                ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
                AccessLimitMode = AccessLimitMode.MaximumSuccessfulAccesses,
                MaxAccessCount = maxReveals
            };
            Db.SecurePages.Add(page);
            await Db.SaveChangesAsync();
            await Encryption.EncryptAndStoreAsync(page, page.ContentArabicHtml, page.ContentEnglishHtml);
            await Db.SaveChangesAsync();
            var credential = new PageCredential { SecurePageId = page.Id, Username = "page-user" };
            credential.PasswordHash = new PasswordHasher<PageCredential>().HashPassword(credential, "Correct!Pass123");
            Db.PageCredentials.Add(credential);
            await Db.SaveChangesAsync();
            page.Credential = credential;

            var delivery = new MobileDelivery
            {
                OrganizationId = organization.Id,
                Organization = organization,
                SecurePageId = page.Id,
                SecurePage = page,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                SentAtUtc = DateTime.UtcNow.AddMinutes(-4),
                DeliveryStatus = "SENT",
                ExpiresAtUtc = deliveryExpiresAtUtc ?? DateTime.UtcNow.AddHours(1),
                RevokedAtUtc = revokedAtUtc
            };
            Db.MobileDeliveries.Add(delivery);
            await Db.SaveChangesAsync();
            return (page, delivery);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}





