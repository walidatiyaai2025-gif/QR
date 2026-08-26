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
    [switch]$RequireFirebaseReady,
    [string]$HostingBundleUrl = 'https://aka.ms/dotnet/10.0/dotnet-hosting-win.exe'
)

. (Join-Path $PSScriptRoot 'QR-IIS.Common.ps1')

Assert-WindowsAdministrator
Enable-RequiredIISFeatures
Import-IISAdministration
Install-DotNet10HostingBundle -DownloadUrl $HostingBundleUrl
Ensure-AppPool -AppPoolName $AppPoolName
New-Item -ItemType Directory -Path $TargetPath -Force | Out-Null
Set-DeploymentPermissions -TargetPath $TargetPath -AppPoolName $AppPoolName
Ensure-SiteAndHttpsBinding -SiteName $SiteName -AppPoolName $AppPoolName -TargetPath $TargetPath -HostName $HostName -CertificatePattern $CertificatePattern

if ($ConfigureHostsFile) { Set-HostsFileEntry -HostName $HostName }

$firebaseMode = Configure-FirebaseCredential -AppPoolName $AppPoolName -TargetPath $TargetPath -FirebaseCredentialPath $FirebaseCredentialPath -GoogleApplicationCredentials $GoogleApplicationCredentials
$effectiveRequireFirebase = $RequireFirebaseReady -or [bool]$FirebaseCredentialPath -or [bool]$GoogleApplicationCredentials
$health = Invoke-Deployment -PublishPath $PublishPath -TargetPath $TargetPath -BackupRoot $BackupRoot -AppPoolName $AppPoolName -HealthUrl "https://$HostName/health" -EnableStdoutLog:$EnableStdoutLog -RequireFirebaseReady:$effectiveRequireFirebase

Write-Host ''
Write-Host 'DA Secure IIS installation completed.'
Write-Host "Site: $SiteName"
Write-Host "AppPool: $AppPoolName"
Write-Host "URL: https://$HostName/"
Write-Host "Application health: $($health.ApplicationStatus)"
Write-Host "Firebase health: $($health.FirebaseStatus) / $($health.FirebaseDetailCode)"
Write-Host "Firebase credential mode: $firebaseMode"
Write-Host 'App_Data, App_Data\keys, SQLite database files, and App_Data\backups were preserved by deployment policy.'
