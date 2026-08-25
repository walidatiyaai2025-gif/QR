[CmdletBinding()]
param(
    [string]$ExpectedFlutterVersion = '3.47.1'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$flutterRoot = Join-Path $repoRoot 'mobile/da_secure'
Push-Location $flutterRoot
try {
    Write-Host '== DA Secure Flutter verification =='
    $versionText = (& flutter --version | Out-String)
    if ($LASTEXITCODE -ne 0) { throw 'flutter --version failed.' }
    if ($versionText -notmatch [regex]::Escape("Flutter $ExpectedFlutterVersion")) {
        throw "Flutter version drift. Expected $ExpectedFlutterVersion."
    }

    flutter pub get
    if ($LASTEXITCODE -ne 0) { throw 'flutter pub get failed.' }

    dart format --output=none --set-exit-if-changed lib test
    if ($LASTEXITCODE -ne 0) { throw 'dart format check failed.' }

    flutter analyze
    if ($LASTEXITCODE -ne 0) { throw 'flutter analyze failed.' }

    flutter test
    if ($LASTEXITCODE -ne 0) { throw 'flutter test failed.' }

    Write-Host 'FLUTTER RELEASE GATE: PASS'
}
finally {
    Pop-Location
}
