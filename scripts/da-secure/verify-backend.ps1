[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $repoRoot
try {
    Write-Host '== DA Secure backend verification =='
    dotnet --version
    dotnet restore SecureQrPortal.sln
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    dotnet build SecureQrPortal.sln --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

    $results = Join-Path $repoRoot 'TestResults/da-secure-release'
    New-Item -ItemType Directory -Force -Path $results | Out-Null
    dotnet test SecureQrPortal.sln --configuration Release --no-build --logger 'trx;LogFileName=da-secure-release-tests.trx' --results-directory $results
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }

    Write-Host 'BACKEND RELEASE GATE: PASS'
}
finally {
    Pop-Location
}
