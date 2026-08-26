#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$BackupPath,
    [string]$BackupRoot = 'C:\inetpub\wwwroot\QR_deployment_backups',
    [string]$TargetPath = 'C:\inetpub\wwwroot\QR',
    [string]$AppPoolName = 'QR',
    [string]$HostName = 'testapi.da.gov.kw',
    [switch]$RequireFirebaseReady
)

. (Join-Path $PSScriptRoot 'QR-IIS.Common.ps1')

Assert-WindowsAdministrator
Import-IISAdministration
if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) { throw "Application pool '$AppPoolName' does not exist." }

if (-not $BackupPath) {
    $latest = Get-LatestDeploymentBackup -BackupRoot $BackupRoot
    if (-not $latest) { throw "No deployment backup was found under '$BackupRoot'." }
    $BackupPath = $latest.FullName
}

$resolvedBackup = (Resolve-Path -LiteralPath $BackupPath).Path
$allowedRoot = [IO.Path]::GetFullPath($BackupRoot).TrimEnd('\') + '\'
if (-not $resolvedBackup.StartsWith($allowedRoot,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Rollback source must be inside the configured deployment backup root.'
}

$currentSafetyBackup = New-DeploymentBackup -TargetPath $TargetPath -BackupRoot $BackupRoot -Label 'pre-rollback'
if ($currentSafetyBackup) { Write-Host "Current payload safety snapshot: $currentSafetyBackup" }
$health = Restore-DeploymentBackup -BackupPath $resolvedBackup -TargetPath $TargetPath -AppPoolName $AppPoolName -HealthUrl "https://$HostName/health" -RequireFirebaseReady:$RequireFirebaseReady

Write-Host ''
Write-Host 'DA Secure IIS rollback completed.'
Write-Host "Restored backup: $resolvedBackup"
Write-Host "Application health: $($health.ApplicationStatus)"
Write-Host "Firebase health: $($health.FirebaseStatus) / $($health.FirebaseDetailCode)"
Write-Host 'App_Data, Data Protection keys, SQLite database files, and App_Data\backups were not restored, deleted, or overwritten.'
