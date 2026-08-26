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
- `keys` — ASP.NET Core Data Protection key ring when the default local key-ring path is used
- `backups` — local SQLite backups
- `database.settings.json` — protected runtime database-provider configuration when present
- temporary/staged restore state when a SQLite restore is pending

Treat the Data Protection key ring as sensitive. Back it up with application security state and restrict ACLs to the administrators and application identities that require access.

### 6.1 Secure Message encryption and key-ring continuity

Secure Message bodies are stored as authenticated ciphertext. Each message has a random data-encryption key that is wrapped by ASP.NET Core Data Protection. The Data Protection key ring is therefore required for every still-valid encrypted Secure Message and for other existing server-protected state.

Single-node default:

`App_Data/keys`

For any multi-node or active/passive deployment, every node that may become active **must use the same durable Data Protection key ring**. Configure the same secured shared path on all nodes using one of:

- configuration key `Security:DataProtectionKeyRingPath`
- environment variable `Security__DataProtectionKeyRingPath`

Example environment value:

```text
\\secure-fileserver\DA-Secure\DataProtectionKeys
```

The shared path must be writable/readable by the application-pool identity under the approved infrastructure identity model. Do not give broad user access. Do not create separate independent key rings on the two application nodes.

If an explicitly configured key-ring path is unavailable or unauthorized, application startup fails instead of silently generating an unrelated local key ring. Correct storage/ACL/connectivity and restart.

Before failover testing, create and successfully reveal a real encrypted Secure Message on node A, switch application traffic to node B, and prove the same still-valid message can be revealed there through the normal authorized flow. This evidence is mandatory before a multi-node release is called verified.

### 6.2 Trusted reverse proxy client IP

Security audit IPs use ASP.NET Core forwarded-header processing. Add only real trusted proxy/load-balancer addresses to `ReverseProxy:KnownProxies` (or `ReverseProxy__KnownProxies__0`, `__1`, etc.).

Never configure arbitrary client networks as trusted proxies merely to make `X-Forwarded-For` appear in logs. If the deployment connects directly to IIS with no proxy, leave the list empty.

After proxy deployment, verify an administrator security-setting change records the real client IP, while a forged forwarded header sent directly by an untrusted client does not override the authoritative connection IP.

## 7. First run

With the default configuration, the application starts on SQLite and automatically applies EF Core migrations.

On startup after the Secure Message encryption feature is introduced, legacy Secure Message rows are converted to authenticated ciphertext before the application begins accepting normal traffic. If that secure migration fails, the application remains unavailable; do not bypass it or restore plaintext behavior.

On the first visit, if no administrator account exists, use the one-time setup page to create the initial administrator. The application creates the `Administrator` role during database initialization. There is no default administrator password in source control.

After setup, sign in through the normal administrator login.

## 8. SQLite configuration

Default database path:

`App_Data/SecureQrPortal.db`

SQLite requires no external database server. Ensure the application-pool identity can modify `App_Data` before the first application start.

For upgrades:

1. back up the current `App_Data` directory and the configured Data Protection key ring if it is external;
2. preserve `App_Data` while replacing application binaries/static files;
3. start/recycle the application pool;
4. allow the application to apply pending migrations and secure legacy-content migration;
5. verify administrator login and a real encrypted QR/Secure Message access path.

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

Also back up the effective Data Protection key ring (`App_Data/keys` by default, or the configured external/shared key-ring path).

### SQL Server

The built-in local-file backup workflow is not the SQL Server backup mechanism. Use SQL Server-native backup/restore procedures and your normal infrastructure retention policy. The Data Protection key ring is separate security state and must also be preserved.

## 12. HTTPS and production hosting

Production should be served only through HTTPS. The application enables HTTPS redirection and HSTS outside Development.

After deployment verify:

- the certificate chain is trusted;
- the certificate matches the host name;
- the HTTPS binding points to the intended certificate;
- HTTP redirects to HTTPS as expected;
- the application pool is started;
- `App_Data` remains writable by the application identity but is not publicly served;
- the effective Data Protection key ring is accessible and durable.

## 13. Configuration and secrets

Repository `appsettings.json` contains safe defaults, including SQLite as the default provider. Environment-specific values should be provided through protected ASP.NET Core configuration sources or the Admin database configuration workflow as appropriate.

Never commit:

- SQL passwords
- administrator passwords
- PFX/P12 files or passwords
- production connection strings
- copied production SQLite databases
- Data Protection keys
- Secure Message wrapped/unwrapped keys or message plaintext

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
- Admin → Settings → Security loads only for Administrator;
- Secure Message Encryption shows ACTIVE by default;
- Secure Message Reveal shows ACTIVE by default;
- a newly generated Secure Message is stored without plaintext body in the database;
- disabling creation requires `DISABLE` and then blocks new/replacement content without modifying existing ciphertext;
- blocking reveal requires `BLOCK-REVEAL`, blocks browser/mobile reveal and does not destroy still-valid message keys;
- re-enabling reveal restores normal authorized access;
- security-setting audit events contain administrator identity, UTC timestamp, old/new state and authoritative client IP, with no message/key/password secrets;
- a newly generated QR resolves through its public token;
- page credential login works;
- counters/access logs update;
- SQLite backup works when SQLite is active;
- Arabic displays RTL and English displays LTR;
- multi-node deployments pass cross-node encrypted-message failover verification.

If any of these checks fail, keep the prior deployment/data/key-ring backup available and do not label the build as the approved v1.0.0 release.

See `SECURE_MESSAGE_ENCRYPTION_CONTROL.md` for the authoritative encryption/reveal security contract and release gates.
