# Secure QR Portal — Windows Server / IIS Installation

## Approved automated installation

Use `Last Approved Patch/Install-QR-IIS.ps1` from an elevated PowerShell session. The script is designed for the approved target:

- `C:\inetpub\wwwroot\QR`
- IIS Site: `API`
- App Pool: `QR`
- Host: `testapi.da.gov.kw`
- Certificate DNS/CN: `*.da.gov.kw`

It will install the Windows IIS roles/features and .NET 10 Hosting Bundle if missing, deploy `Build/SecureQrPortal-v1.0.0-publish.zip`, preserve runtime `App_Data`, configure IIS/HTTPS/ACLs/hosts/firewall, start the site, validate the local URL, and launch it.

## Certificate prerequisite

The server must have a valid `*.da.gov.kw` certificate with a private key in `LocalMachine\My` or `LocalMachine\WebHosting`. If it is not installed, place the authorized `.pfx` in the same `Last Approved Patch` folder before running the script. The script can import it after securely prompting for the PFX password.

## SQLite permissions

The `QR` application pool identity receives Modify permission only on runtime-write locations (`App_Data` and `logs`) and read/execute permission on the application files.

## SQL Server 2022

SQLite is the safe default. SQL Server 2022 can be configured from the Admin database settings after successful connection testing. Do not place production database credentials in the repository.
