[CmdletBinding()]
param(
    [string]$PhysicalPath = 'C:\inetpub\wwwroot\QR',
    [string]$SiteName = 'API',
    [string]$AppPoolName = 'QR',
    [string]$HostName = 'testapi.da.gov.kw',
    [string]$CertificateName = '*.da.gov.kw',
    [int]$HttpsPort = 443,
    [int]$HttpPort = 80,
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
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this PowerShell script as Administrator.'
    }
}

function Test-AspNetCore10Runtime {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) { return $false }
    $runtimes = & dotnet --list-runtimes 2>$null
    return [bool]($runtimes | Select-String -SimpleMatch 'Microsoft.AspNetCore.App 10.0.')
}

function Install-DotNetHostingBundle10 {
    if ($SkipHostingBundleInstall) {
        if (-not (Test-AspNetCore10Runtime)) { throw '.NET 10 ASP.NET Core runtime is missing and -SkipHostingBundleInstall was specified.' }
        return
    }
    if (Test-AspNetCore10Runtime) {
        Write-Ok '.NET 10 ASP.NET Core runtime already installed.'
        return
    }

    Write-Step 'Installing .NET 10 ASP.NET Core Hosting Bundle'
    $url = 'https://aka.ms/dotnet/10.0/dotnet-hosting-win.exe'
    $installer = Join-Path $env:TEMP 'dotnet-hosting-10-win.exe'
    Invoke-WebRequest -Uri $url -OutFile $installer -UseBasicParsing
    $proc = Start-Process -FilePath $installer -ArgumentList '/install','/quiet','/norestart' -Wait -PassThru
    if ($proc.ExitCode -notin 0, 3010) { throw "Hosting Bundle installer failed with exit code $($proc.ExitCode)." }
    Remove-Item $installer -Force -ErrorAction SilentlyContinue
    if (-not (Test-AspNetCore10Runtime)) {
        Write-Warn 'Hosting Bundle installation completed, but the current process cannot yet see the runtime. IIS will still be restarted; a server reboot may be required if deployment validation fails.'
    }
    Write-Ok '.NET 10 Hosting Bundle installation completed.'
}

function Install-IisFeatures {
    Write-Step 'Installing/verifying IIS features'
    Import-Module ServerManager
    $features = @(
        'Web-Server','Web-WebServer','Web-Common-Http','Web-Default-Doc','Web-Static-Content',
        'Web-Http-Errors','Web-Http-Logging','Web-Request-Monitor','Web-Stat-Compression',
        'Web-Filtering','Web-AppInit','Web-Mgmt-Tools','Web-Mgmt-Console'
    )
    $result = Install-WindowsFeature -Name $features -IncludeManagementTools
    if (-not $result.Success) { throw 'One or more IIS Windows features failed to install.' }
    Import-Module WebAdministration
    Write-Ok 'IIS features are installed.'
}

function Resolve-PublishZip {
    if ($PublishZip) {
        $resolved = Resolve-Path -LiteralPath $PublishZip -ErrorAction Stop
        return $resolved.Path
    }
    $candidate = Join-Path $PSScriptRoot 'Build\SecureQrPortal-v1.0.0-publish.zip'
    if (Test-Path -LiteralPath $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
    $fallback = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Build') -Filter '*publish*.zip' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($fallback) { return $fallback.FullName }
    throw "Published build ZIP not found. Expected: $candidate"
}

function Backup-ExistingDeployment {
    if (-not (Test-Path -LiteralPath $PhysicalPath)) { return $null }
    $root = Join-Path (Split-Path $PhysicalPath -Parent) 'QR_Backups'
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backup = Join-Path $root "QR-$stamp"
    New-Item -ItemType Directory -Path $backup -Force | Out-Null

    $appData = Join-Path $PhysicalPath 'App_Data'
    if (Test-Path -LiteralPath $appData) {
        Copy-Item -LiteralPath $appData -Destination (Join-Path $backup 'App_Data') -Recurse -Force
    }
    foreach ($name in @('web.config','appsettings.json','appsettings.Production.json')) {
        $path = Join-Path $PhysicalPath $name
        if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $backup -Force }
    }
    Write-Ok "Existing runtime data/config backup: $backup"
    return $backup
}

function Deploy-Build([string]$ZipPath) {
    Write-Step 'Deploying approved Release build'
    $temp = Join-Path $env:TEMP ("SecureQrPortal-Deploy-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $temp -Force

    $source = $temp
    $children = Get-ChildItem -LiteralPath $temp -Force
    if ($children.Count -eq 1 -and $children[0].PSIsContainer) {
        $candidateWebConfig = Join-Path $children[0].FullName 'web.config'
        $candidateDll = Join-Path $children[0].FullName 'SecureQrPortal.dll'
        if ((Test-Path $candidateWebConfig) -or (Test-Path $candidateDll)) { $source = $children[0].FullName }
    }

    if (-not (Test-Path (Join-Path $source 'SecureQrPortal.dll'))) {
        throw 'The publish ZIP does not contain SecureQrPortal.dll at its deployment root.'
    }

    New-Item -ItemType Directory -Path $PhysicalPath -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $PhysicalPath 'App_Data') -Force | Out-Null

    # Mirror application files while preserving runtime database, Data Protection keys, and backups.
    $args = @(
        ('"' + $source + '"'), ('"' + $PhysicalPath + '"'), '/MIR', '/R:2', '/W:2',
        '/XD', ('"' + (Join-Path $source 'App_Data') + '"'),
        '/XF', '*.db', '*.db-shm', '*.db-wal', '/NFL', '/NDL', '/NP'
    )
    $p = Start-Process -FilePath 'robocopy.exe' -ArgumentList $args -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ge 8) { throw "Robocopy failed with exit code $($p.ExitCode)." }

    foreach ($dir in @('App_Data','App_Data\keys','App_Data\backups','logs')) {
        New-Item -ItemType Directory -Path (Join-Path $PhysicalPath $dir) -Force | Out-Null
    }
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    Write-Ok "Application deployed to $PhysicalPath"
}

function Configure-HostsFile {
    Write-Step 'Configuring hosts file'
    $hosts = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
    $lines = Get-Content -LiteralPath $hosts -ErrorAction Stop
    $escaped = [regex]::Escape($HostName)
    $filtered = $lines | Where-Object { $_ -notmatch "^\s*\d{1,3}(?:\.\d{1,3}){3}\s+$escaped(?:\s|$)" }
    $filtered += "127.0.0.1`t$HostName"
    Set-Content -LiteralPath $hosts -Value $filtered -Encoding ascii
    ipconfig /flushdns | Out-Null
    Write-Ok "hosts: 127.0.0.1 $HostName"
}

function Resolve-Certificate {
    Write-Step "Locating HTTPS certificate $CertificateName"
    $stores = @('Cert:\LocalMachine\My','Cert:\LocalMachine\WebHosting')
    $cert = foreach ($store in $stores) {
        if (Test-Path $store) {
            Get-ChildItem $store | Where-Object {
                $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) -and (
                    $_.Subject -like "CN=$CertificateName*" -or
                    ($_.DnsNameList -and ($_.DnsNameList.Unicode -contains $CertificateName))
                )
            }
        }
    } | Sort-Object NotAfter -Descending | Select-Object -First 1

    if (-not $cert) {
        $pfx = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.pfx' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if ($pfx) {
            Write-Warn "Certificate not installed. PFX found: $($pfx.FullName). Importing to LocalMachine\My."
            $password = Read-Host 'Enter PFX password' -AsSecureString
            $cert = Import-PfxCertificate -FilePath $pfx.FullName -CertStoreLocation 'Cert:\LocalMachine\My' -Password $password -Exportable:$false
        }
    }

    if (-not $cert) {
        throw "A valid $CertificateName certificate with private key was not found in LocalMachine\My or LocalMachine\WebHosting, and no PFX was found beside this script."
    }
    Write-Ok "Certificate: $($cert.Subject), expires $($cert.NotAfter.ToString('u'))"
    return $cert
}

function Configure-Iis([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate) {
    Write-Step 'Configuring IIS application pool and site'
    Import-Module WebAdministration

    if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
        New-WebAppPool -Name $AppPoolName | Out-Null
    }
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ''
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedPipelineMode -Value 'Integrated'
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.identityType -Value 4
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name startMode -Value 'AlwaysRunning'
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.loadUserProfile -Value $true

    if (-not (Test-Path "IIS:\Sites\$SiteName")) {
        New-Website -Name $SiteName -PhysicalPath $PhysicalPath -ApplicationPool $AppPoolName -Port $HttpPort -HostHeader $HostName | Out-Null
    } else {
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $PhysicalPath
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
    }

    Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $SiteName -Filter 'system.applicationHost/sites/site/applicationDefaults' -Name preloadEnabled -Value $true -ErrorAction SilentlyContinue

    $httpBinding = Get-WebBinding -Name $SiteName -Protocol 'http' | Where-Object { $_.bindingInformation -eq "*:$HttpPort:$HostName" }
    if (-not $httpBinding) { New-WebBinding -Name $SiteName -Protocol 'http' -Port $HttpPort -HostHeader $HostName | Out-Null }

    $httpsBinding = Get-WebBinding -Name $SiteName -Protocol 'https' | Where-Object { $_.bindingInformation -eq "*:$HttpsPort:$HostName" }
    if (-not $httpsBinding) {
        New-WebBinding -Name $SiteName -Protocol 'https' -Port $HttpsPort -HostHeader $HostName -SslFlags 1 | Out-Null
        $httpsBinding = Get-WebBinding -Name $SiteName -Protocol 'https' | Where-Object { $_.bindingInformation -eq "*:$HttpsPort:$HostName" }
    }

    $storeName = if ($Certificate.PSParentPath -like '*WebHosting*') { 'WebHosting' } else { 'My' }
    $httpsBinding.AddSslCertificate($Certificate.Thumbprint, $storeName)

    Write-Ok "IIS site '$SiteName' -> $PhysicalPath, app pool '$AppPoolName'."
}

function Configure-Permissions {
    Write-Step 'Configuring filesystem permissions'
    $identity = "IIS AppPool\$AppPoolName"
    & icacls.exe $PhysicalPath /inheritance:e | Out-Null
    & icacls.exe $PhysicalPath /grant:r "$identity:(OI)(CI)(RX)" /T /C | Out-Null
    foreach ($dir in @('App_Data','logs')) {
        $path = Join-Path $PhysicalPath $dir
        New-Item -ItemType Directory -Path $path -Force | Out-Null
        & icacls.exe $path /grant:r "$identity:(OI)(CI)(M)" /T /C | Out-Null
    }
    Write-Ok "ACLs configured for $identity."
}

function Configure-Firewall {
    Write-Step 'Configuring Windows Firewall rules'
    foreach ($port in @($HttpPort,$HttpsPort)) {
        $name = "Secure QR Portal IIS TCP $port"
        if (-not (Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue)) {
            New-NetFirewallRule -DisplayName $name -Direction Inbound -Action Allow -Protocol TCP -LocalPort $port -Profile Any | Out-Null
        }
    }
    Write-Ok 'Firewall rules verified.'
}

function Validate-Deployment {
    Write-Step 'Starting IIS and validating deployment'
    iisreset /restart | Out-Null
    Start-Sleep -Seconds 3
    Start-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
    Start-Website -Name $SiteName -ErrorAction SilentlyContinue

    $healthUrl = "https://$HostName/health"
    $url = "https://$HostName/"
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $false
    $client = New-Object System.Net.Http.HttpClient($handler)
    try {
        $response = $client.GetAsync($healthUrl).GetAwaiter().GetResult()
        $code = [int]$response.StatusCode
        if ($code -lt 200 -or $code -ge 500) { throw "HTTP validation returned status $code." }
        Write-Ok "HTTPS validation returned HTTP $code at $healthUrl"
    } finally {
        $client.Dispose()
        $handler.Dispose()
    }
    if (-not $SkipBrowserOpen) { Start-Process $url }
}

Assert-Administrator
Write-Host 'Secure QR Portal v1.0.0 — Approved IIS Installer' -ForegroundColor White
Write-Host "Target: https://$HostName/ | Site=$SiteName | AppPool=$AppPoolName | Path=$PhysicalPath" -ForegroundColor Gray

Install-IisFeatures
Install-DotNetHostingBundle10
$zip = Resolve-PublishZip
Write-Ok "Build package: $zip"

Import-Module WebAdministration
if (Test-Path "IIS:\Sites\$SiteName") { Stop-Website -Name $SiteName -ErrorAction SilentlyContinue }
if (Test-Path "IIS:\AppPools\$AppPoolName") { Stop-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue }
Backup-ExistingDeployment | Out-Null
Deploy-Build -ZipPath $zip
Configure-Permissions
Configure-HostsFile
$cert = Resolve-Certificate
Configure-Iis -Certificate $cert
Configure-Firewall
Validate-Deployment

Write-Host "`nDEPLOYMENT COMPLETE: https://$HostName/" -ForegroundColor Green
