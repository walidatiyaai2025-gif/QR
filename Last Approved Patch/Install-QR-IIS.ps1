[CmdletBinding()]
param(
    [string]$PhysicalPath = 'C:\inetpub\wwwroot\QR',
    [string]$SiteName = 'API',
    [string]$AppPoolName = 'QR',
    [string]$HostName = 'testapi.da.gov.kw',
    [string]$CertificateName = '*.da.gov.kw',
    [int]$HttpPort = 80,
    [int]$HttpsPort = 443,
    [string]$PublishZip = '',
    [switch]$SkipHostingBundleInstall,
    [switch]$SkipBrowserOpen
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Write-Step([string]$Message) { Write-Host "`n==> $Message" -ForegroundColor Cyan }
function Write-Ok([string]$Message) { Write-Host "[OK] $Message" -ForegroundColor Green }
function Write-Warn([string]$Message) { Write-Warning $Message }

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run Windows PowerShell as Administrator.' }
}

function Install-IisFeatures {
    Write-Step 'Installing/verifying IIS and management tools'
    Import-Module ServerManager -ErrorAction Stop
    $features = @('Web-Server','Web-WebServer','Web-Common-Http','Web-Default-Doc','Web-Static-Content','Web-Http-Errors','Web-Http-Logging','Web-Request-Monitor','Web-Stat-Compression','Web-Filtering','Web-AppInit','Web-Mgmt-Tools','Web-Mgmt-Console','Web-Scripting-Tools')
    $result = Install-WindowsFeature -Name $features -IncludeManagementTools
    if (-not $result.Success) { throw 'IIS feature installation failed.' }
    Import-Module WebAdministration -ErrorAction Stop
    Write-Ok 'IIS and WebAdministration are available.'
}

function Test-AspNetCore10Runtime {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) { return $false }
    return [bool]((& dotnet --list-runtimes 2>$null) | Select-String -SimpleMatch 'Microsoft.AspNetCore.App 10.0.')
}

function Install-DotNetHostingBundle10 {
    if (Test-AspNetCore10Runtime) { Write-Ok '.NET 10 ASP.NET Core runtime already installed.'; return }
    if ($SkipHostingBundleInstall) { throw '.NET 10 ASP.NET Core runtime is missing.' }
    Write-Step 'Installing .NET 10 ASP.NET Core Hosting Bundle'
    $installer = Join-Path $env:TEMP 'dotnet-hosting-10-win.exe'
    Invoke-WebRequest 'https://aka.ms/dotnet/10.0/dotnet-hosting-win.exe' -OutFile $installer -UseBasicParsing
    $process = Start-Process $installer -ArgumentList '/install','/quiet','/norestart' -Wait -PassThru
    if ($process.ExitCode -notin 0,3010) { throw "Hosting Bundle failed with exit code $($process.ExitCode)." }
    Remove-Item $installer -Force -ErrorAction SilentlyContinue
    Write-Ok '.NET 10 Hosting Bundle installation completed.'
}

function Resolve-PublishZip {
    if ($PublishZip) { return (Resolve-Path -LiteralPath $PublishZip -ErrorAction Stop).Path }
    $candidate = Join-Path $PSScriptRoot 'Build\SecureQrPortal-v1.0.0-publish.zip'
    if (Test-Path -LiteralPath $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
    $fallback = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Build') -Filter '*publish*.zip' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($fallback) { return $fallback.FullName }
    throw "Published build ZIP not found. Expected: $candidate"
}

function Ensure-AppPool {
    Write-Step "Configuring IIS App Pool '$AppPoolName'"
    Import-Module WebAdministration -ErrorAction Stop
    if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) { New-WebAppPool -Name $AppPoolName | Out-Null }
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ''
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedPipelineMode -Value 'Integrated'
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.identityType -Value 4
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.loadUserProfile -Value $true
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name startMode -Value 'AlwaysRunning'
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
    Write-Ok "App Pool '$AppPoolName' configured."
}

function Stop-Safely {
    Import-Module WebAdministration -ErrorAction Stop
    if ((Test-Path "IIS:\Sites\$SiteName") -and ((Get-Website -Name $SiteName).State -eq 'Started')) { Stop-Website -Name $SiteName }
    if ((Test-Path "IIS:\AppPools\$AppPoolName") -and ((Get-WebAppPoolState -Name $AppPoolName).Value -eq 'Started')) { Stop-WebAppPool -Name $AppPoolName }
}

function Backup-ExistingDeployment {
    if (-not (Test-Path -LiteralPath $PhysicalPath)) { return }
    $root = 'C:\inetpub\wwwroot\QR_Backups'
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $destination = Join-Path $root ("QR-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($name in @('App_Data','web.config','appsettings.json','appsettings.Production.json')) {
        $source = Join-Path $PhysicalPath $name
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force }
    }
    Write-Ok "Backup created: $destination"
}

function Deploy-Build([string]$ZipPath) {
    Write-Step 'Deploying published build to IIS physical path'
    $temp = Join-Path $env:TEMP ("QRDeploy-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $temp -Force
    $source = $temp
    $children = @(Get-ChildItem -LiteralPath $temp -Force)
    if ($children.Count -eq 1 -and $children[0].PSIsContainer -and (Test-Path (Join-Path $children[0].FullName 'SecureQrPortal.dll'))) { $source = $children[0].FullName }
    if (-not (Test-Path (Join-Path $source 'SecureQrPortal.dll'))) { throw 'SecureQrPortal.dll was not found in publish package.' }
    New-Item -ItemType Directory -Path $PhysicalPath -Force | Out-Null
    $roboArgs = @($source,$PhysicalPath,'/MIR','/R:2','/W:2','/XD',(Join-Path $source 'App_Data'),'/XF','*.db','*.db-shm','*.db-wal','/NFL','/NDL','/NP')
    $robocopy = Start-Process 'robocopy.exe' -ArgumentList $roboArgs -Wait -PassThru -NoNewWindow
    if ($robocopy.ExitCode -ge 8) { throw "Robocopy failed with exit code $($robocopy.ExitCode)." }
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
    Write-Ok "Application deployed to $PhysicalPath"
}

function Configure-Permissions {
    Write-Step 'Configuring production NTFS permissions'
    $identity = "IIS AppPool\$AppPoolName"
    & icacls.exe $PhysicalPath /inheritance:e | Out-Null
    & icacls.exe $PhysicalPath /grant:r "${identity}:(OI)(CI)(RX)" /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Unable to grant base permissions to $identity." }
    $writable = @((Join-Path $PhysicalPath 'App_Data'),(Join-Path $PhysicalPath 'App_Data\keys'),(Join-Path $PhysicalPath 'App_Data\backups'),(Join-Path $PhysicalPath 'logs'),(Join-Path $PhysicalPath 'wwwroot\uploads'),(Join-Path $PhysicalPath 'wwwroot\uploads\logos'))
    foreach ($directory in $writable) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        & icacls.exe $directory /grant:r "${identity}:(OI)(CI)(M)" /T /C | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Unable to grant Modify permission on $directory to $identity." }
    }
    Write-Ok 'Runtime database, logs, uploads and organization-logo permissions configured.'
}

function Ensure-LocalResolution {
    Write-Step "Verifying local resolution for $HostName"
    try { $ips = @([System.Net.Dns]::GetHostAddresses($HostName) | ForEach-Object { $_.IPAddressToString }) } catch { $ips = @() }
    if ($ips -contains '127.0.0.1') { Write-Ok "$HostName resolves to 127.0.0.1."; return }
    $hosts = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
    $pattern = '^\s*127\.0\.0\.1\s+' + [regex]::Escape($HostName) + '(\s|$)'
    if (Select-String -LiteralPath $hosts -Pattern $pattern -Quiet -ErrorAction SilentlyContinue) { ipconfig /flushdns | Out-Null; Write-Ok 'Required hosts entry already exists.'; return }
    for ($attempt=1; $attempt -le 5; $attempt++) {
        try { Add-Content -LiteralPath $hosts -Value "127.0.0.1`t$HostName" -Encoding ASCII -ErrorAction Stop; ipconfig /flushdns | Out-Null; Write-Ok "hosts entry added: 127.0.0.1 $HostName"; return } catch { Start-Sleep -Seconds 2 }
    }
    try { $ips = @([System.Net.Dns]::GetHostAddresses($HostName) | ForEach-Object { $_.IPAddressToString }) } catch { $ips = @() }
    if ($ips -contains '127.0.0.1') { Write-Warn 'hosts file is locked, but the hostname already resolves to loopback; continuing.'; return }
    throw "Could not configure $HostName to resolve locally."
}

function Resolve-Certificate {
    Write-Step "Locating certificate $CertificateName"
    $certificates = @()
    foreach ($store in @('Cert:\LocalMachine\My','Cert:\LocalMachine\WebHosting')) {
        if (Test-Path $store) { $certificates += @(Get-ChildItem $store | Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) -and ($_.Subject -like "CN=$CertificateName*" -or ($_.DnsNameList -and ($_.DnsNameList.Unicode -contains $CertificateName))) }) }
    }
    $certificate = $certificates | Sort-Object NotAfter -Descending | Select-Object -First 1
    if (-not $certificate) {
        $pfx = Get-ChildItem $PSScriptRoot -Filter '*.pfx' -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($pfx) { $password = Read-Host 'Certificate PFX password' -AsSecureString; $certificate = Import-PfxCertificate -FilePath $pfx.FullName -CertStoreLocation 'Cert:\LocalMachine\My' -Password $password }
    }
    if (-not $certificate) { throw "Valid $CertificateName certificate with private key not found." }
    Write-Ok "Certificate found: $($certificate.Subject), expires $($certificate.NotAfter.ToString('u'))"
    return $certificate
}

function Configure-IisSite([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate) {
    Write-Step "Configuring IIS Website '$SiteName'"
    Import-Module WebAdministration -ErrorAction Stop
    if (-not (Test-Path "IIS:\Sites\$SiteName")) { New-Website -Name $SiteName -PhysicalPath $PhysicalPath -ApplicationPool $AppPoolName -Port $HttpPort -HostHeader $HostName | Out-Null } else { Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $PhysicalPath; Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName }
    $httpInfo = "*:${HttpPort}:${HostName}"
    if (-not (Get-WebBinding -Name $SiteName -Protocol 'http' | Where-Object { $_.bindingInformation -eq $httpInfo })) { New-WebBinding -Name $SiteName -Protocol 'http' -Port $HttpPort -HostHeader $HostName | Out-Null }
    $httpsInfo = "*:${HttpsPort}:${HostName}"
    $httpsBinding = Get-WebBinding -Name $SiteName -Protocol 'https' | Where-Object { $_.bindingInformation -eq $httpsInfo }
    if (-not $httpsBinding) { New-WebBinding -Name $SiteName -Protocol 'https' -Port $HttpsPort -HostHeader $HostName -SslFlags 1 | Out-Null; $httpsBinding = Get-WebBinding -Name $SiteName -Protocol 'https' | Where-Object { $_.bindingInformation -eq $httpsInfo } }
    if (-not $httpsBinding) { throw "HTTPS binding creation failed: $httpsInfo" }
    $storeName = if ($Certificate.PSParentPath -like '*WebHosting*') { 'WebHosting' } else { 'My' }
    $httpsBinding.AddSslCertificate($Certificate.Thumbprint, $storeName)
    Write-Ok "IIS Website '$SiteName' -> $PhysicalPath using App Pool '$AppPoolName'."
}

function Configure-Firewall {
    Write-Step 'Verifying firewall rules'
    foreach ($port in @($HttpPort,$HttpsPort)) { $name = "Secure QR Portal IIS TCP $port"; if (-not (Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue)) { New-NetFirewallRule -DisplayName $name -Direction Inbound -Action Allow -Protocol TCP -LocalPort $port -Profile Any | Out-Null } }
    Write-Ok 'Firewall rules verified.'
}

function Start-Safely {
    Import-Module WebAdministration -ErrorAction Stop
    if ((Get-WebAppPoolState -Name $AppPoolName).Value -ne 'Started') { Start-WebAppPool -Name $AppPoolName }
    if ((Get-Website -Name $SiteName).State -ne 'Started') { Start-Website -Name $SiteName }
}

function Validate-Deployment {
    Write-Step 'Restarting IIS and running deployment checks'
    iisreset /restart | Out-Null
    Start-Sleep -Seconds 4
    Start-Safely
    $site = Get-Website -Name $SiteName
    $pool = Get-WebAppPoolState -Name $AppPoolName
    if ($site.State -ne 'Started') { throw "IIS Site $SiteName is not Started." }
    if ($pool.Value -ne 'Started') { throw "App Pool $AppPoolName is not Started." }
    $healthUrl = "https://$HostName/health"
    try { $response = Invoke-WebRequest $healthUrl -UseBasicParsing -TimeoutSec 20; $code = [int]$response.StatusCode } catch { if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode.value__ } else { throw } }
    if ($code -lt 200 -or $code -ge 500) { throw "Health check failed with HTTP $code." }
    Write-Ok "IIS Site: $SiteName = Started"
    Write-Ok "App Pool: $AppPoolName = Started"
    Write-Ok "HTTPS health: $healthUrl = HTTP $code"
    if (-not $SkipBrowserOpen) { Start-Process "https://$HostName/" }
}

Assert-Administrator
Write-Host 'Secure QR Portal - Approved Windows Server IIS Installer' -ForegroundColor White
Write-Host "Site=$SiteName | AppPool=$AppPoolName | Path=$PhysicalPath | Host=$HostName" -ForegroundColor Gray
Install-IisFeatures
Install-DotNetHostingBundle10
$zip = Resolve-PublishZip
Write-Ok "Build package: $zip"
Ensure-AppPool
Stop-Safely
Backup-ExistingDeployment
Deploy-Build -ZipPath $zip
Configure-Permissions
Ensure-LocalResolution
$certificate = Resolve-Certificate
Configure-IisSite -Certificate $certificate
Configure-Firewall
Start-Safely
Validate-Deployment
Write-Host "`nDEPLOYMENT COMPLETE: https://$HostName/" -ForegroundColor Green
Write-Host "IIS Manager -> Sites -> $SiteName" -ForegroundColor Green
Write-Host "IIS Manager -> Application Pools -> $AppPoolName" -ForegroundColor Green
