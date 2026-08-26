#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$PublishPath = (Join-Path $PSScriptRoot 'publish'),
    [string]$TargetPath = 'C:\inetpub\wwwroot\QR',
    [string]$SiteName = 'API',
    [string]$AppPoolName = 'QR',
    [string]$HostName = 'testapi.da.gov.kw',
    [string]$CertificatePattern = '*.da.gov.kw',
    [string]$BackupRoot = 'C:\inetpub\wwwroot\QR_deployment_backups',
    [string]$FirebaseCredentialPath,
    [string]$GoogleApplicationCredentials,
    [switch]$ConfigureHostsFile,
    [switch]$EnableStdoutLog,
    [switch]$RequireFirebaseReady
)

. (Join-Path $PSScriptRoot 'QR-IIS.Common.ps1')

Assert-WindowsAdministrator
Import-IISAdministration
if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) { throw "Application pool '$AppPoolName' does not exist. Run Install-QR-IIS.ps1 first." }
if (-not (Test-Path "IIS:\Sites\$SiteName")) { throw "IIS site '$SiteName' does not exist. Run Install-QR-IIS.ps1 first." }
if (-not (Test-DotNet10Hosting)) { throw '.NET 10 Hosting Bundle / ASP.NET Core Module V2 is missing. Run Install-QR-IIS.ps1 first.' }

Ensure-AppPool -AppPoolName $AppPoolName
Ensure-SiteAndHttpsBinding -SiteName $SiteName -AppPoolName $AppPoolName -TargetPath $TargetPath -HostName $HostName -CertificatePattern $CertificatePattern
if ($ConfigureHostsFile) { Set-HostsFileEntry -HostName $HostName }

$firebaseMode = Configure-FirebaseCredential -AppPoolName $AppPoolName -TargetPath $TargetPath -FirebaseCredentialPath $FirebaseCredentialPath -GoogleApplicationCredentials $GoogleApplicationCredentials
$effectiveRequireFirebase = $RequireFirebaseReady -or [bool]$FirebaseCredentialPath -or [bool]$GoogleApplicationCredentials
$health = Invoke-Deployment -PublishPath $PublishPath -TargetPath $TargetPath -BackupRoot $BackupRoot -AppPoolName $AppPoolName -HealthUrl "https://$HostName/health" -EnableStdoutLog:$EnableStdoutLog -RequireFirebaseReady:$effectiveRequireFirebase

Write-Host ''
Write-Host 'DA Secure IIS update completed.'
Write-Host "Application health: $($health.ApplicationStatus)"
Write-Host "Firebase health: $($health.FirebaseStatus) / $($health.FirebaseDetailCode)"
Write-Host "Firebase credential mode: $firebaseMode"
Write-Host 'The pre-update payload was backed up; App_Data was never mirrored or deleted.'
