# Secure QR Portal v1.0.0

Secure QR Portal is an ASP.NET Core MVC administration and secure-content portal for issuing cryptographically secure QR links, managing organizations and Secure Pages, auditing access, and centrally administering every QR code from **QR Management → QR Code Registry**.

## Platform

- .NET 10 (`net10.0`; repository SDK pin: `10.0.400`)
- ASP.NET Core MVC + Razor Views
- ASP.NET Core Identity
- Entity Framework Core 10
- SQLite by default: `App_Data/SecureQrPortal.db`
- Microsoft SQL Server 2022 optional provider
- Arabic RTL and English LTR infrastructure

## Prerequisites

For development/build:

- .NET 10 SDK compatible with `global.json`
- PowerShell or a normal shell capable of running `dotnet`

For IIS production deployment:

- Windows Server with IIS
- Administrator rights for initial IIS/ACL/hosts/binding configuration
- .NET 10 ASP.NET Core Hosting Bundle (the approved installer can install it when required)
- A valid HTTPS certificate for the configured host
- SQL Server 2022 only when the SQL Server provider is selected

## Build and test

```powershell
dotnet --info
dotnet restore SecureQrPortal.sln
dotnet build SecureQrPortal.sln -c Release --no-restore
dotnet test SecureQrPortal.sln -c Release --no-build
dotnet run --project src/SecureQrPortal/SecureQrPortal.csproj
```

## First run and administrator setup

SQLite is the default provider. The application creates the required `App_Data` subdirectories, applies EF Core migrations at startup, creates the `Administrator` role, and initializes default application settings.

When no users exist, the application exposes the one-time first-run administrator setup flow. Create the first administrator there; no default or production administrator password is committed to source control. After the first account exists, the setup flow redirects to the normal administrator login.

## SQLite

Default database:

`App_Data/SecureQrPortal.db`

The application applies the current EF Core migration automatically at startup. The IIS application-pool identity must have modify permission on `App_Data` because the application writes the SQLite database, Data Protection keys, runtime database settings, backups, and staged restore files there.

Do **not** replace or delete an existing production `App_Data` directory during application upgrades. Back it up before deployment changes.

## SQL Server 2022 provider

SQLite remains the default even though the SQL Server provider is installed. Provider selection is explicit.

From **Admin → Settings → Database** you can:

1. enter SQL Server connection settings;
2. test connectivity;
3. initialize/migrate the SQL Server database;
4. save SQL Server as the active provider;
5. restart the application so the selected provider is loaded on startup.

The saved SQL Server connection string is protected with ASP.NET Core Data Protection before it is written to `App_Data/database.settings.json`. Protect and back up `App_Data/keys`; losing that key ring can make the protected connection string unreadable. If protected runtime SQL configuration cannot be read, startup fails closed to SQLite instead of silently deleting or replacing the SQLite database.

Switching providers does not delete the SQLite database. Use SQL Server-native backup/restore tooling for SQL Server production databases.

## Migrations

The repository contains EF Core migrations under `src/SecureQrPortal/Migrations`.

- The selected provider is migrated automatically on application startup.
- SQL Server can also be initialized from the administrator Database screen before switching providers.
- Schema changes must be committed as EF Core migrations and validated against both provider paths; do not hand-edit a production schema as a substitute for a migration.

## Backup and restore

The built-in backup workflow is for SQLite mode:

- backups are written to `App_Data/backups`;
- generated backups are integrity-checked before they are accepted;
- uploaded restore databases are validated and staged as a pending restore;
- the staged restore is applied during application startup.

Back up `App_Data/keys` together with operational data. SQL Server deployments must use SQL Server-native backup/restore procedures in addition to normal application/configuration backups.

## IIS approved deployment

The repository contains the deployment handoff folder:

`Last Approved Patch`

The approved CI process produces:

- `Build/SecureQrPortal-v1.0.0-publish.zip` — IIS-ready Release publish output
- `Install-QR-IIS.ps1` — Windows Server/IIS setup and deployment script
- `BUILD-MANIFEST.json` — source SHA, workflow run, framework, and target metadata

Current deployment defaults are:

- Physical path: `C:\inetpub\wwwroot\QR`
- IIS site: `API`
- Application pool: `QR`
- Host: `testapi.da.gov.kw`
- Hosts mapping: `127.0.0.1 testapi.da.gov.kw`
- Certificate subject: `*.da.gov.kw`

Run PowerShell **as Administrator** from `Last Approved Patch`:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\Install-QR-IIS.ps1
```

The installer is designed to preserve existing `App_Data`, configure IIS and filesystem permissions, bind HTTPS, start the site, validate the HTTPS endpoint, and open the configured URL. See `INSTALL.md` for the full deployment and rollback checklist.

## HTTPS and Data Protection

Production uses HTTPS redirection and HSTS outside Development. Keep the production certificate valid and renew it before expiry.

ASP.NET Core Data Protection keys are persisted under `App_Data/keys`. They protect application secrets such as recoverable QR-token material and protected database configuration. Treat this directory as sensitive operational state: restrict ACLs, back it up securely, and restore it with the application data when recovering a server.

## Configuration and secrets

`appsettings.json` contains non-secret defaults only. Do not commit production passwords, SQL credentials, certificate private keys, or other secrets.

Runtime provider configuration is stored under `App_Data`. Environment-specific configuration may be supplied through ASP.NET Core configuration sources/environment variables as appropriate for the deployment environment.

## CI and release artifacts

`.github/workflows/ci.yml` is the v1 release gate. It performs:

1. repository checkout;
2. .NET 10 SDK setup and `dotnet --info`;
3. solution restore;
4. Release build;
5. Release tests;
6. IIS publish;
7. publish ZIP packaging;
8. source ZIP packaging;
9. source, publish, and test-result artifact upload.

Compiler or test failures fail the workflow; the workflow does not use `continue-on-error` to mask product failures.

Release artifacts are named:

- `SecureQrPortal-v1.0.0-source`
- `SecureQrPortal-v1.0.0-publish`
- `SecureQrPortal-v1.0.0-test-results`

A release is not approved solely because a ZIP exists. Use an artifact generated from the exact release source SHA whose restore, build, and tests are all green.

## Security baseline

- Public QR URLs use cryptographically secure random tokens; sequential internal QR references are not authentication tokens.
- Stored public-token lookup values are SHA-256 hashes; protected recoverable token material is handled through Data Protection.
- Secure Page passwords are hashed and are not exported/displayed.
- Administrator authentication uses ASP.NET Core Identity and role authorization.
- HTML content is sanitized server-side before storage.
- Public login requests are rate limited.
- Organization-logo uploads are size/type/signature validated and stored under generated names.
- Production secrets and SQL credentials must never be committed to the repository.
