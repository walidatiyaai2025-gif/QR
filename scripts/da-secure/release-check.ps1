[CmdletBinding()]
param(
    [switch]$SkipApiSmoke
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Write-Host '=== DA Secure one-command release validation ==='
$started = Get-Date

& (Join-Path $PSScriptRoot 'verify-backend.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Backend release gate failed.' }

& (Join-Path $PSScriptRoot 'verify-flutter.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Flutter release gate failed.' }

& (Join-Path $PSScriptRoot 'verify-android.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Android project audit failed.' }

& (Join-Path $PSScriptRoot 'security-check.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Security regression gate failed.' }

if (-not $SkipApiSmoke) {
    & (Join-Path $PSScriptRoot 'api-smoke.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Canonical API smoke failed.' }
}
else {
    Write-Host 'CANONICAL API SMOKE: EXPLICITLY SKIPPED'
}

& (Join-Path $PSScriptRoot 'build-apk.ps1')
if ($LASTEXITCODE -ne 0) { throw 'APK build failed.' }

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
$branch = (& git -C $repoRoot branch --show-current).Trim()
$elapsed = (Get-Date) - $started

Write-Host '=== RELEASE HARNESS: PASS ==='
Write-Host "Source branch: $branch"
Write-Host "Commit: $commit"
Write-Host ('Elapsed: {0:mm\:ss}' -f $elapsed)
