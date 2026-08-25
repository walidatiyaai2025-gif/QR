# DA Secure — Current Mobile State

## Bootstrap baseline

- Repository: `https://github.com/walidatiyaai2025-gif/QR`
- Default branch: `main`
- Main HEAD used to create isolated mobile branch: `2b144ec93d3802214dbe0d80f0077808f9ede346`
- Isolated branch: `feat/secure-qr-mobile-app-isolated`
- Open PRs at bootstrap: none
- Main HEAD itself has no combined status checks because it is a `[skip ci]` release-refresh commit.
- Latest observed CI run on its parent `2e27bc5cf447ac7e89de4dfe39338b1bb6fcbac6`: `Secure QR Portal CI` run #76, successful.

**Always fetch the live isolated-branch HEAD before work. Do not trust a SHA copied into a status document.**

## Backend found on bootstrap

The repository is an ASP.NET Core / EF Core / Identity solution targeting .NET SDK `10.0.400`.

Existing production surfaces include:

- `Organization` with Arabic/English names, logo path, active/demo state. It does **not** yet contain a registered mobile number.
- `SecurePage` with organization ownership, QR reference/token, Arabic/English title/content, validity/expiry, access-limit modes, successful access/open/login counters, revocation, audit relationships.
- `PageCredential` with server-stored username plus password hash; one credential record per secure page.
- QR registry/admin controllers and details/index/print views.
- Public QR and QR share flows.
- `SecurePageAccessService` containing server-side credential verification, access-policy checks, counters, and access logging.
- Existing rich Text Editor fields for Arabic and English sanitized secure-page HTML.
- `SmsGatewayService` with Kuwait normalization and configured provider architecture; current appsettings has SMS disabled and credential values empty.
- `AuditService` plus `AuditLog`/`AccessLog` and visible admin audit/access views.
- ASP.NET Core Identity administrator login, lockout, rate limiting, setup flow, change password.
- Admin dashboard and organizations UI.
- Runtime database architecture supporting SQLite by default and protected SQL Server configuration.

## Database architecture

Default: SQLite at `App_Data/SecureQrPortal.db`.

Optional: SQL Server. Connection string is stored protected using ASP.NET Core Data Protection. Runtime falls closed to SQLite if protected settings cannot be loaded.

## Mobile bootstrap status

Created under `mobile/da_secure/`:

- Flutter package manifest and source layout.
- Central API configuration targeting `https://testapi.da.gov.kw`.
- Routing shell.
- DA Secure navy/gold design-system foundation.
- Splash and initial screen shells without fake production data.
- Networking/auth/secure-storage/Firebase architecture placeholders.
- Android package namespace/application id `com.qr.mobile.da`.
- Attached Firebase Android client configuration in `android/app/google-services.json`.

The execution environment used for bootstrap did **not** have Flutter or .NET SDK binaries, so `flutter create`, `flutter pub get`, Android Gradle wrapper generation, Flutter analyze/tests, .NET build/tests, APK build, device installation, FCM delivery, and live API E2E were not executed here. These remain UNVERIFIED until run in a capable worker environment.

## Visual asset status

The approved screenshots and official crest supplied by the owner are authoritative. The GitHub write connector used for this bootstrap was used for source/text configuration; the binary reference images were not silently replaced by the repository's existing demo SVG. Workers must not treat `wwwroot/images/sample/diwan-logo.svg` as the approved mobile crest because it explicitly identifies itself as a demo asset.

## Current status vocabulary

VERIFIED here means only repository facts actually inspected or writes confirmed by GitHub. It does not mean runtime/mobile functionality is verified.
