[CmdletBinding()]
param(
    [string]$ExpectedFlutterVersion = '3.47.1'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$flutterRoot = Join-Path $repoRoot 'mobile/da_secure'
$apkPath = Join-Path $flutterRoot 'build/app/outputs/flutter-apk/app-debug.apk'

Push-Location $flutterRoot
try {
    $versionText = (& flutter --version | Out-String)
    if ($LASTEXITCODE -ne 0) { throw 'flutter --version failed.' }
    if ($versionText -notmatch [regex]::Escape("Flutter $ExpectedFlutterVersion")) {
        throw "Flutter version drift. Expected $ExpectedFlutterVersion."
    }

    flutter pub get
    if ($LASTEXITCODE -ne 0) { throw 'flutter pub get failed.' }

    # Do not bypass Flutter's Android dependency validation.
    flutter build apk --debug
    if ($LASTEXITCODE -ne 0) { throw 'APK BUILD: FAIL.' }
    if (-not (Test-Path $apkPath)) { throw 'APK BUILD: FAIL - expected artifact missing.' }

    $commit = (& git -C $repoRoot rev-parse HEAD).Trim()
    Write-Host 'APK: PASS'
    Write-Host "APK path: $apkPath"
    Write-Host "Source commit: $commit"
}
finally {
    Pop-Location
}
