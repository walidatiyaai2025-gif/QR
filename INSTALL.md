# Secure QR Portal — Windows Server / IIS Installation

This document covers the supported Secure QR Portal v1.0.0 deployment paths for SQLite and SQL Server 2022.

## 1. Prerequisites

For the approved IIS deployment you need:

- Windows Server with Administrator access
- IIS and ASP.NET Core Module support
- .NET 10 ASP.NET Core Hosting Bundle
- a valid HTTPS certificate for the configured host
- DNS/hosts resolution for the site name
- the CI-generated `SecureQrPortal-v1.0.0-publish.zip`
- SQL Server 2022 only if the SQL Server provider will be used

The approved installer can enable the required IIS Windows features and install the .NET 10 Hosting Bundle when it is missing.

## 2. Approved deployment target

Current defaults:

- Physical path: `C:\inetpub\wwwroot\QR`
- IIS site: `API`
- Application pool: `QR`
- Host: `testapi.da.gov.kw`
- Hosts entry: `127.0.0.1 testapi.da.gov.kw`
- Certificate subject: `*.da.gov.kw`

The application pool must use **No Managed Code** because ASP.NET Core is hosted through the ASP.NET Core Module.

## 3. Release artifact verification

Use a publish ZIP produced by a green GitHub Actions run for the exact release source SHA. The release workflow also publishes the source ZIP and test results.

Expected handoff files under `Last Approved Patch`:

- `Build/SecureQrPortal-v1.0.0-publish.zip`
- `Install-QR-IIS.ps1`
- `BUILD-MANIFEST.json`
- `README.md`

Before deployment, confirm the manifest source SHA is the release candidate you intend to install.

## 4. Certificate prerequisite

The server must have a valid certificate with a private key in `LocalMachine\My` or `LocalMachine\WebHosting` whose DNS/CN matches the configured host.

For the approved target this is `*.da.gov.kw`. If the authorized PFX is not already installed, place it beside `Install-QR-IIS.ps1`; the installer can import it after securely prompting for the PFX password. Never commit a PFX/P12 file or its password to Git.

## 5. Automated IIS installation

Open PowerShell **as Administrator**, change to `Last Approved Patch`, then run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\Install-QR-IIS.ps1
```

The installer is intended to be safe to re-run. It deploys the published build, preserves existing runtime `App_Data`, configures IIS/site/app-pool settings, applies required filesystem permissions, configures host/binding/firewall requirements, starts the site, validates HTTPS, and opens the configured URL.

Do not manually copy a new package over a running production `App_Data` directory.

## 6. App_Data permissions and protected state

The IIS application-pool identity needs **Modify** permission on:

- `App_Data`
- `logs`

Application binaries/static content need read/execute access only.

Important runtime state under `App_Data` includes:

- `SecureQrPortal.db` — default SQLite database
- `keys` — ASP.NET Core Data Protection key ring
- `backups` — local SQLite backups
- `database.settings.json` — protected runtime database-provider configuration when present
- temporary/staged restore state when a SQLite restore is pending

Treat `App_Data/keys` as sensitive. Back it up with the application data and restrict ACLs to the administrators and application identity that require access.

## 7. First run

With the default configuration, the application starts on SQLite and automatically applies EF Core migrations.

On the first visit, if no administrator account exists, use the one-time setup page to create the initial administrator. The application creates the `Administrator` role during database initialization. There is no default administrator password in source control.

After setup, sign in through the normal administrator login.

## 8. SQLite configuration

Default database path:

`App_Data/SecureQrPortal.db`

SQLite requires no external database server. Ensure the application-pool identity can modify `App_Data` before the first application start.

For upgrades:

1. back up the current `App_Data` directory;
2. preserve `App_Data` while replacing application binaries/static files;
3. start/recycle the application pool;
4. allow the application to apply pending migrations;
5. verify administrator login and a real QR access path.

## 9. SQL Server 2022 configuration

SQLite remains the default until an administrator explicitly switches the provider.

Recommended sequence from **Admin → Settings → Database**:

1. enter SQL Server 2022 host/database/authentication settings;
2. test the connection;
3. initialize the SQL Server schema;
4. save SQL Server as the selected provider;
5. recycle/restart the application so the runtime provider selection is reloaded;
6. verify administrator login and expected application data on the selected database.

The application protects the saved SQL connection string through ASP.NET Core Data Protection before storing it in `App_Data/database.settings.json`. Do not place production credentials in `appsettings.json`, source control, scripts, issue comments, or CI logs.

Changing to SQL Server does not delete or overwrite the existing SQLite database. Use SQL Server-native backup, restore, HA, and maintenance procedures for SQL Server production data.

## 10. Migrations

The application calls EF Core migration on startup for the selected provider. SQL Server initialization from the Admin Database screen also applies the repository migrations to the target SQL Server database before provider switching.

If startup fails during migration, do not bypass the failure or edit the database manually to make the application start. Capture the exact migration/error, correct the migration/configuration issue, and redeploy a validated build.

## 11. Backup and restore

### SQLite

The Admin backup feature creates local SQLite backups in `App_Data/backups` and verifies database integrity before accepting them.

A restore upload is validated and staged; the pending restore is applied during application startup. After staging a restore, perform the required application restart/recycle and verify the restored database before normal use.

Also back up the Data Protection key ring in `App_Data/keys`.

### SQL Server

The built-in local-file backup workflow is not the SQL Server backup mechanism. Use SQL Server-native backup/restore procedures and your normal infrastructure retention policy.

## 12. HTTPS and production hosting

Production should be served only through HTTPS. The application enables HTTPS redirection and HSTS outside Development.

After deployment verify:

- the certificate chain is trusted;
- the certificate matches the host name;
- the HTTPS binding points to the intended certificate;
- HTTP redirects to HTTPS as expected;
- the application pool is started;
- `App_Data` remains writable by the application identity but is not publicly served.

## 13. Configuration and secrets

Repository `appsettings.json` contains safe defaults, including SQLite as the default provider. Environment-specific values should be provided through protected ASP.NET Core configuration sources or the Admin database configuration workflow as appropriate.

Never commit:

- SQL passwords
- administrator passwords
- PFX/P12 files or passwords
- production connection strings
- copied production SQLite databases
- Data Protection keys

## 14. CI and release gate

`.github/workflows/ci.yml` must be green for the exact release source SHA. It runs:

1. checkout;
2. .NET SDK setup;
3. `dotnet --info`;
4. restore;
5. Release build;
6. Release tests;
7. publish;
8. publish ZIP packaging;
9. source ZIP packaging;
10. source/publish/test-result artifact upload.

Do not promote a release when restore, build, or tests are red, even if an older publish ZIP exists in the repository.

## 15. Post-deployment verification

After installation or upgrade verify at minimum:

- HTTPS site loads successfully;
- first-run setup is unavailable once an administrator exists;
- administrator login works;
- Organizations and Secure Pages load;
- QR Code Registry loads;
- a newly generated QR resolves through its public token;
- page credential login works;
- counters/access logs update;
- SQLite backup works when SQLite is active;
- Arabic displays RTL and English displays LTR.

If any of these checks fail, keep the prior deployment/data backup available and do not label the build as the approved v1.0.0 release.
