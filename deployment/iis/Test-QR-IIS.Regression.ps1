#requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$commonPath = Join-Path $scriptRoot 'QR-IIS.Common.ps1'
$updatePath = Join-Path $scriptRoot 'Update-QR-IIS.ps1'

. $commonPath

function Assert-True {
    param([Parameter(Mandatory)][bool]$Condition,[Parameter(Mandatory)][string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Equal {
    param($Expected,$Actual,[Parameter(Mandatory)][string]$Message)
    if ($Expected -ne $Actual) { throw "$Message Expected='$Expected' Actual='$Actual'" }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("da-secure-iis-regression-{0}" -f [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

try {
    Write-Host '[REGRESSION] Relative PublishPath resolves from deployment script directory.'
    $packageRoot = Join-Path $tempRoot 'package'
    New-Item -ItemType Directory -Path (Join-Path $packageRoot 'publish') -Force | Out-Null
    $resolved = Resolve-ScriptRelativePath -Path '.\publish' -ScriptRoot $packageRoot
    $expected = [IO.Path]::GetFullPath((Join-Path $packageRoot '.\publish'))
    Assert-Equal -Expected $expected -Actual $resolved -Message 'Relative PublishPath did not resolve from ScriptRoot.'

    Write-Host '[REGRESSION] Root-level published web.config layout is supported.'
    $rootLayout = Join-Path $tempRoot 'root-layout'
    New-Item -ItemType Directory -Path $rootLayout -Force | Out-Null
    @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <aspNetCore processPath="dotnet" arguments=".\SecureQrPortal.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" />
  </system.webServer>
</configuration>
'@ | Set-Content -LiteralPath (Join-Path $rootLayout 'web.config') -Encoding utf8
    Set-StdoutDiagnostics -StagePath $rootLayout -Enable:$true
    [xml]$rootXml = Get-Content -LiteralPath (Join-Path $rootLayout 'web.config') -Raw
    $rootNode = $rootXml.SelectSingleNode('/configuration/system.webServer/aspNetCore')
    Assert-True -Condition ($null -ne $rootNode) -Message 'Root-level aspNetCore node disappeared.'
    Assert-Equal -Expected 'true' -Actual $rootNode.GetAttribute('stdoutLogEnabled') -Message 'Root-level stdoutLogEnabled was not updated.'
    Assert-Equal -Expected '.\App_Data\logs\stdout' -Actual $rootNode.GetAttribute('stdoutLogFile') -Message 'Root-level stdoutLogFile was not updated.'

    Write-Host '[REGRESSION] location-wrapped published web.config layout is supported.'
    $locationLayout = Join-Path $tempRoot 'location-layout'
    New-Item -ItemType Directory -Path $locationLayout -Force | Out-Null
    @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <aspNetCore processPath="dotnet" arguments=".\SecureQrPortal.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" />
    </system.webServer>
  </location>
</configuration>
'@ | Set-Content -LiteralPath (Join-Path $locationLayout 'web.config') -Encoding utf8
    Set-StdoutDiagnostics -StagePath $locationLayout -Enable:$false
    [xml]$locationXml = Get-Content -LiteralPath (Join-Path $locationLayout 'web.config') -Raw
    $locationNode = $locationXml.SelectSingleNode('/configuration/location/system.webServer/aspNetCore')
    Assert-True -Condition ($null -ne $locationNode) -Message 'Location-wrapped aspNetCore node disappeared.'
    Assert-Equal -Expected 'false' -Actual $locationNode.GetAttribute('stdoutLogEnabled') -Message 'Location-wrapped stdoutLogEnabled was not updated.'
    Assert-Equal -Expected '.\App_Data\logs\stdout' -Actual $locationNode.GetAttribute('stdoutLogFile') -Message 'Location-wrapped stdoutLogFile was not updated.'

    Write-Host '[REGRESSION] Service-account JSON remains rejected from publish payload.'
    $secretPayload = Join-Path $tempRoot 'secret-payload'
    New-Item -ItemType Directory -Path $secretPayload -Force | Out-Null
    '{"type":"service_account","private_key":"SECRET","client_email":"firebase@example.invalid"}' |
        Set-Content -LiteralPath (Join-Path $secretPayload 'service-account.json') -Encoding utf8
    $secretRejected = $false
    try {
        Assert-NoFirebaseCredentialInPublish -PublishPath $secretPayload
    } catch {
        $secretRejected = $true
    }
    Assert-True -Condition $secretRejected -Message 'Service-account JSON was not rejected.'

    Write-Host '[STATIC] HTTPS update must reuse a correct existing binding/certificate.'
    $common = Get-Content -LiteralPath $commonPath -Raw
    foreach ($marker in @(
        'Reusing existing HTTPS binding',
        'Existing HTTPS certificate is valid for the configured host and will be preserved.',
        'Existing HTTPS certificate binding already matches the required certificate.',
        'PayloadReplacementStarted',
        'AppPoolStoppedByDeployment',
        'BackupCreated',
        "'/XD'",
        "'App_Data'"
    )) {
        Assert-True -Condition $common.Contains($marker) -Message "Missing required hotfix marker: $marker"
    }
    Assert-True -Condition (-not ($common -match '(?s)Get-WebBinding.+?Protocol https.+?Remove-WebBinding\s*\r?\n\s*New-WebBinding')) -Message 'HTTPS binding is still unconditionally removed and recreated.'
    foreach ($tlsBypassMarker in @(
        ('ServerCertificate' + 'ValidationCallback'),
        ('SkipCertificate' + 'Check')
    )) {
        Assert-True -Condition (-not $common.Contains($tlsBypassMarker)) -Message "TLS certificate verification bypass detected: $tlsBypassMarker"
    }

    Write-Host '[STATIC] Update script explicitly normalizes PublishPath with PSScriptRoot.'
    $update = Get-Content -LiteralPath $updatePath -Raw
    Assert-True -Condition $update.Contains('Resolve-ScriptRelativePath -Path $PublishPath -ScriptRoot $PSScriptRoot') -Message 'Update script does not resolve relative PublishPath from PSScriptRoot.'

    Write-Host 'DA Secure IIS regression checks: PASS'
} finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
