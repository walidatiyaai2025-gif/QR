Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "[DA Secure IIS] $Message"
}

function Assert-WindowsAdministrator {
    if ($PSVersionTable.PSVersion.Major -ge 6 -and -not $IsWindows) {
        throw 'DA Secure IIS deployment scripts must run on Windows.'
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this PowerShell session as Administrator.'
    }
}

function Import-IISAdministration {
    if (-not (Get-Module -ListAvailable -Name WebAdministration)) {
        throw 'The WebAdministration PowerShell module is unavailable. Enable IIS Management Scripting Tools first.'
    }
    Import-Module WebAdministration -ErrorAction Stop
}

function Enable-RequiredIISFeatures {
    Write-Step 'Ensuring required IIS features are enabled.'

    if (Get-Command Install-WindowsFeature -ErrorAction SilentlyContinue) {
        $features = @(
            'Web-Server','Web-WebServer','Web-Common-Http','Web-Default-Doc','Web-Static-Content',
            'Web-Http-Errors','Web-Health','Web-Http-Logging','Web-Performance','Web-Stat-Compression',
            'Web-Security','Web-Filtering','Web-App-Dev','Web-Net-Ext45','Web-ASP-Net45',
            'Web-ISAPI-Ext','Web-ISAPI-Filter','Web-Mgmt-Tools','Web-Mgmt-Console','Web-Scripting-Tools'
        )
        $result = Install-WindowsFeature -Name $features -IncludeManagementTools
        if (-not $result.Success) { throw 'Windows Server IIS feature installation failed.' }
        return
    }

    $features = @(
        'IIS-WebServerRole','IIS-WebServer','IIS-CommonHttpFeatures','IIS-DefaultDocument',
        'IIS-StaticContent','IIS-HttpErrors','IIS-HttpLogging','IIS-RequestFiltering',
        'IIS-ISAPIExtensions','IIS-ISAPIFilter','IIS-ASPNET45','IIS-ManagementConsole',
        'IIS-ManagementScriptingTools'
    )
    foreach ($feature in $features | Select-Object -Unique) {
        $state = Get-WindowsOptionalFeature -Online -FeatureName $feature -ErrorAction SilentlyContinue
        if ($state -and $state.State -ne 'Enabled') {
            Enable-WindowsOptionalFeature -Online -FeatureName $feature -All -NoRestart | Out-Null
        }
    }
}

function Test-DotNet10Hosting {
    $runtimeFound = $false
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        $runtimeFound = [bool]((& dotnet --list-runtimes 2>$null) | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App 10\.' })
    }

    $moduleFound = $false
    try {
        Import-Module WebAdministration -ErrorAction Stop
        $moduleFound = [bool](Get-WebGlobalModule -Name 'AspNetCoreModuleV2' -ErrorAction SilentlyContinue)
    } catch {
        $moduleFound = $false
    }

    return ($runtimeFound -and $moduleFound)
}

function Install-DotNet10HostingBundle {
    param([string]$DownloadUrl = 'https://aka.ms/dotnet/10.0/dotnet-hosting-win.exe')

    if (Test-DotNet10Hosting) {
        Write-Step '.NET 10 ASP.NET Core Hosting Bundle already available.'
        return
    }

    Write-Step 'Installing .NET 10 ASP.NET Core Hosting Bundle.'
    $tempInstaller = Join-Path ([IO.Path]::GetTempPath()) ("dotnet-hosting-10-{0}.exe" -f [guid]::NewGuid())
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $DownloadUrl -OutFile $tempInstaller -UseBasicParsing
        if ((Get-Item $tempInstaller).Length -lt 1MB) { throw 'Hosting Bundle download is unexpectedly small.' }

        $process = Start-Process -FilePath $tempInstaller -ArgumentList '/install','/quiet','/norestart' -Wait -PassThru
        if ($process.ExitCode -notin @(0,1641,3010)) {
            throw "Hosting Bundle installer exited with code $($process.ExitCode)."
        }

        & "$env:SystemRoot\System32\iisreset.exe" /restart | Out-Null
        Start-Sleep -Seconds 2
        if (-not (Test-DotNet10Hosting)) {
            throw '.NET 10 Hosting Bundle installation completed but ASP.NET Core Module V2/runtime verification failed.'
        }
    } finally {
        Remove-Item -LiteralPath $tempInstaller -Force -ErrorAction SilentlyContinue
    }
}

function Ensure-AppPool {
    param([Parameter(Mandatory)][string]$AppPoolName)

    Import-IISAdministration
    if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
        Write-Step "Creating IIS application pool '$AppPoolName'."
        New-WebAppPool -Name $AppPoolName | Out-Null
    } else {
        Write-Step "Reusing IIS application pool '$AppPoolName'."
    }

    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ''
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedPipelineMode -Value 'Integrated'
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.identityType -Value 4
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name startMode -Value 'AlwaysRunning'
}

function Get-EligibleCertificate {
    param(
        [Parameter(Mandatory)][string]$CertificatePattern,
        [Parameter(Mandatory)][string]$HostName
    )

    $now = Get-Date
    $certificate = Get-ChildItem Cert:\LocalMachine\My | Where-Object {
        $_.HasPrivateKey -and $_.NotAfter -gt $now.AddMinutes(5) -and (
            $_.Subject -match [regex]::Escape("CN=$CertificatePattern") -or
            ($_.DnsNameList -and ($_.DnsNameList.Unicode -contains $CertificatePattern)) -or
            ($_.DnsNameList -and ($_.DnsNameList.Unicode -contains $HostName))
        )
    } | Sort-Object NotAfter -Descending | Select-Object -First 1

    if (-not $certificate) {
        throw "No valid LocalMachine\\My certificate matching '$CertificatePattern' or '$HostName' with a private key was found."
    }
    return $certificate
}

function Ensure-SiteAndHttpsBinding {
    param(
        [Parameter(Mandatory)][string]$SiteName,
        [Parameter(Mandatory)][string]$AppPoolName,
        [Parameter(Mandatory)][string]$TargetPath,
        [Parameter(Mandatory)][string]$HostName,
        [Parameter(Mandatory)][string]$CertificatePattern
    )

    Import-IISAdministration
    $certificate = Get-EligibleCertificate -CertificatePattern $CertificatePattern -HostName $HostName

    if (-not (Test-Path "IIS:\Sites\$SiteName")) {
        Write-Step "Creating IIS site '$SiteName'."
        New-Website -Name $SiteName -PhysicalPath $TargetPath -ApplicationPool $AppPoolName -Port 80 -HostHeader $HostName | Out-Null
    } else {
        Write-Step "Reusing IIS site '$SiteName'."
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $TargetPath
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
    }

    Get-WebBinding -Name $SiteName -Protocol https -ErrorAction SilentlyContinue |
        Where-Object { $_.bindingInformation -eq "*:443:$HostName" } |
        Remove-WebBinding
    New-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName -SslFlags 1

    $sslPath = "IIS:\SslBindings\0.0.0.0!443!$HostName"
    Remove-Item -LiteralPath $sslPath -Force -ErrorAction SilentlyContinue
    Get-Item "Cert:\LocalMachine\My\$($certificate.Thumbprint)" |
        New-Item -Path $sslPath -SSLFlags 1 -Force | Out-Null

    Get-WebBinding -Name $SiteName -Protocol http -ErrorAction SilentlyContinue |
        Where-Object { $_.bindingInformation -eq "*:80:$HostName" } |
        Remove-WebBinding

    $suffix = $certificate.Thumbprint.Substring([Math]::Max(0, $certificate.Thumbprint.Length - 8))
    Write-Step "HTTPS binding configured for $HostName with certificate thumbprint ending $suffix."
}

function Set-HostsFileEntry {
    param([Parameter(Mandatory)][string]$HostName,[string]$Address = '127.0.0.1')

    $hostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
    $backupPath = "$hostsPath.da-secure-backup-$(Get-Date -Format 'yyyyMMddHHmmss')"
    Copy-Item -LiteralPath $hostsPath -Destination $backupPath -Force

    $escapedHost = [regex]::Escape($HostName)
    $filtered = Get-Content -LiteralPath $hostsPath -ErrorAction Stop | Where-Object {
        $_ -notmatch "^\s*(?:\d{1,3}\.){3}\d{1,3}\s+$escapedHost(?:\s|$)"
    }
    Set-Content -LiteralPath $hostsPath -Value @($filtered,"$Address`t$HostName`t# DA Secure IIS") -Encoding ascii
    Write-Step "Hosts file entry configured for $HostName; previous hosts file was backed up."
}

function Set-AppPoolEnvironmentVariable {
    param(
        [Parameter(Mandatory)][string]$AppPoolName,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Value
    )

    $filter = "system.applicationHost/applicationPools/add[@name='$AppPoolName']/environmentVariables"
    $existing = Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter "$filter/add[@name='$Name']" -Name 'value' -ErrorAction SilentlyContinue
    if ($null -eq $existing) {
        Add-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter $filter -Name '.' -Value @{ name = $Name; value = $Value }
    } else {
        Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter "$filter/add[@name='$Name']" -Name 'value' -Value $Value
    }
}

function Assert-CredentialPathIsExternal {
    param(
        [Parameter(Mandatory)][string]$CredentialPath,
        [Parameter(Mandatory)][string]$TargetPath
    )

    $credentialFullPath = [IO.Path]::GetFullPath($CredentialPath)
    $targetFullPath = [IO.Path]::GetFullPath($TargetPath).TrimEnd('\') + '\'
    if ($credentialFullPath.StartsWith($targetFullPath,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Firebase service-account credentials must live outside the IIS wwwroot deployment path.'
    }
    if (-not (Test-Path -LiteralPath $credentialFullPath -PathType Leaf)) {
        throw 'The configured Firebase credential file does not exist.'
    }
    return $credentialFullPath
}

function Configure-FirebaseCredential {
    param(
        [Parameter(Mandatory)][string]$AppPoolName,
        [Parameter(Mandatory)][string]$TargetPath,
        [string]$FirebaseCredentialPath,
        [string]$GoogleApplicationCredentials
    )

    if ($FirebaseCredentialPath -and $GoogleApplicationCredentials) {
        throw 'Specify FirebaseCredentialPath or GoogleApplicationCredentials, not both.'
    }

    if ($FirebaseCredentialPath) {
        $external = Assert-CredentialPathIsExternal -CredentialPath $FirebaseCredentialPath -TargetPath $TargetPath
        Set-AppPoolEnvironmentVariable -AppPoolName $AppPoolName -Name 'Firebase__CredentialPath' -Value $external
        Write-Step 'Firebase:CredentialPath configured through an IIS AppPool environment variable; credential content was not copied or logged.'
        return 'Firebase:CredentialPath'
    }
    if ($GoogleApplicationCredentials) {
        $external = Assert-CredentialPathIsExternal -CredentialPath $GoogleApplicationCredentials -TargetPath $TargetPath
        Set-AppPoolEnvironmentVariable -AppPoolName $AppPoolName -Name 'GOOGLE_APPLICATION_CREDENTIALS' -Value $external
        Write-Step 'GOOGLE_APPLICATION_CREDENTIALS configured through an IIS AppPool environment variable; credential content was not copied or logged.'
        return 'GOOGLE_APPLICATION_CREDENTIALS'
    }

    Write-Step 'Firebase credential variables were left unchanged.'
    return 'UNCHANGED'
}

function Invoke-RobocopyChecked {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination,
        [string[]]$ExtraArguments = @()
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $arguments = @($Source,$Destination,'/MIR','/COPY:DAT','/DCOPY:DAT','/R:2','/W:2','/NFL','/NDL','/NP','/NJH','/NJS') + $ExtraArguments
    & robocopy.exe @arguments | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Robocopy failed with exit code $LASTEXITCODE." }
}

function New-DeploymentBackup {
    param(
        [Parameter(Mandatory)][string]$TargetPath,
        [Parameter(Mandatory)][string]$BackupRoot,
        [string]$Label = 'deploy'
    )

    if (-not (Test-Path -LiteralPath $TargetPath -PathType Container)) { return $null }
    $hasPayload = Get-ChildItem -LiteralPath $TargetPath -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne 'App_Data' } | Select-Object -First 1
    if (-not $hasPayload) { return $null }

    New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null
    $backupPath = Join-Path $BackupRoot ("{0}-{1}" -f $Label,(Get-Date -Format 'yyyyMMdd-HHmmss'))
    Write-Step "Backing up current deployment to $backupPath (App_Data excluded)."
    Invoke-RobocopyChecked -Source $TargetPath -Destination $backupPath -ExtraArguments @('/XD',(Join-Path $TargetPath 'App_Data'))

    @{ createdAtUtc = [DateTime]::UtcNow.ToString('o'); targetPath = $TargetPath; appDataExcluded = $true } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backupPath '.deployment-backup.json') -Encoding utf8
    return $backupPath
}

function Copy-PreservedConfiguration {
    param([Parameter(Mandatory)][string]$TargetPath,[Parameter(Mandatory)][string]$StagePath)

    if (-not (Test-Path -LiteralPath $TargetPath)) { return }
    Get-ChildItem -LiteralPath $TargetPath -Filter 'appsettings*.json' -File -ErrorAction SilentlyContinue | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $StagePath $_.Name) -Force
    }
    foreach ($relative in @('.env','appsettings.Local.json','appsettings.Secrets.json')) {
        $existing = Join-Path $TargetPath $relative
        if (Test-Path -LiteralPath $existing -PathType Leaf) {
            Copy-Item -LiteralPath $existing -Destination (Join-Path $StagePath $relative) -Force
        }
    }
}

function Assert-NoFirebaseCredentialInPublish {
    param([Parameter(Mandatory)][string]$PublishPath)

    Get-ChildItem -LiteralPath $PublishPath -Filter '*.json' -File -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
        $text = Get-Content -LiteralPath $_.FullName -Raw -ErrorAction Stop
        if ($text -match '"private_key"\s*:' -and $text -match '"client_email"\s*:') {
            throw "Publish payload appears to contain a Firebase/Google service-account credential JSON: $($_.Name). Remove it before deployment."
        }
    }
}

function Set-StdoutDiagnostics {
    param([Parameter(Mandatory)][string]$StagePath,[Parameter(Mandatory)][bool]$Enable)

    $webConfig = Join-Path $StagePath 'web.config'
    if (-not (Test-Path -LiteralPath $webConfig -PathType Leaf)) { throw 'Published web.config was not found.' }
    [xml]$xml = Get-Content -LiteralPath $webConfig -Raw
    $aspNetCore = $xml.configuration.'system.webServer'.aspNetCore
    if (-not $aspNetCore) { throw 'Published web.config does not contain system.webServer/aspNetCore.' }

    $aspNetCore.stdoutLogEnabled = if ($Enable) { 'true' } else { 'false' }
    $aspNetCore.stdoutLogFile = '.\App_Data\logs\stdout'
    $xml.Save($webConfig)
    Write-Step ("ASP.NET Core stdout diagnostics are {0}." -f $(if ($Enable) { 'ENABLED' } else { 'disabled' }))
}

function Set-DeploymentPermissions {
    param([Parameter(Mandatory)][string]$TargetPath,[Parameter(Mandatory)][string]$AppPoolName)

    $identity = "IIS AppPool\$AppPoolName"
    $appData = Join-Path $TargetPath 'App_Data'
    New-Item -ItemType Directory -Path $TargetPath -Force | Out-Null
    New-Item -ItemType Directory -Path $appData -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $appData 'keys') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $appData 'backups') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $appData 'logs') -Force | Out-Null

    & icacls.exe $TargetPath /grant:r "${identity}:(OI)(CI)(RX)" /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to configure read/execute permissions for the application pool identity.' }
    & icacls.exe $appData /grant:r "${identity}:(OI)(CI)(M)" /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to configure modify permissions for App_Data.' }
}

function Stop-AppPoolSafely {
    param([Parameter(Mandatory)][string]$AppPoolName)
    Import-IISAdministration
    if (Test-Path "IIS:\AppPools\$AppPoolName") {
        if ((Get-WebAppPoolState -Name $AppPoolName).Value -ne 'Stopped') {
            Stop-WebAppPool -Name $AppPoolName
            Start-Sleep -Seconds 1
        }
    }
}

function Start-AppPoolSafely {
    param([Parameter(Mandatory)][string]$AppPoolName)
    Import-IISAdministration
    Start-WebAppPool -Name $AppPoolName
}

function Wait-ForHealth {
    param(
        [Parameter(Mandatory)][string]$HealthUrl,
        [switch]$RequireFirebaseReady,
        [int]$Attempts = 12,
        [int]$DelaySeconds = 5
    )

    $lastError = $null
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            $response = Invoke-RestMethod -Uri $HealthUrl -Method Get -TimeoutSec 15
            if ($response.status -ne 'ok') { throw "Application health status is '$($response.status)'." }

            $firebaseStatus = [string]$response.pushProvider.status
            $firebaseDetail = [string]$response.pushProvider.detailCode
            Write-Step "Health PASS; Firebase status=$firebaseStatus detail=$firebaseDetail."
            if ($RequireFirebaseReady -and $firebaseStatus -ne 'READY') {
                throw "Firebase health is '$firebaseStatus' ($firebaseDetail), but READY is required."
            }

            return [pscustomobject]@{
                ApplicationStatus = [string]$response.status
                Version = [string]$response.version
                FirebaseStatus = $firebaseStatus
                FirebaseDetailCode = $firebaseDetail
            }
        } catch {
            $lastError = $_
            if ($attempt -lt $Attempts) { Start-Sleep -Seconds $DelaySeconds }
        }
    }
    throw "Health check failed after $Attempts attempts. $($lastError.Exception.Message)"
}

function Restore-DeploymentBackup {
    param(
        [Parameter(Mandatory)][string]$BackupPath,
        [Parameter(Mandatory)][string]$TargetPath,
        [Parameter(Mandatory)][string]$AppPoolName,
        [Parameter(Mandatory)][string]$HealthUrl,
        [switch]$RequireFirebaseReady
    )

    if (-not (Test-Path -LiteralPath (Join-Path $BackupPath '.deployment-backup.json') -PathType Leaf)) {
        throw "Backup path '$BackupPath' is not a DA Secure deployment backup."
    }

    Write-Step "Rolling back application payload from $BackupPath; App_Data remains untouched."
    Stop-AppPoolSafely -AppPoolName $AppPoolName
    Invoke-RobocopyChecked -Source $BackupPath -Destination $TargetPath -ExtraArguments @('/XD',(Join-Path $TargetPath 'App_Data'),'/XF','.deployment-backup.json')
    Set-DeploymentPermissions -TargetPath $TargetPath -AppPoolName $AppPoolName
    Start-AppPoolSafely -AppPoolName $AppPoolName
    return Wait-ForHealth -HealthUrl $HealthUrl -RequireFirebaseReady:$RequireFirebaseReady
}

function Invoke-Deployment {
    param(
        [Parameter(Mandatory)][string]$PublishPath,
        [Parameter(Mandatory)][string]$TargetPath,
        [Parameter(Mandatory)][string]$BackupRoot,
        [Parameter(Mandatory)][string]$AppPoolName,
        [Parameter(Mandatory)][string]$HealthUrl,
        [switch]$EnableStdoutLog,
        [switch]$RequireFirebaseReady
    )

    $publishFull = [IO.Path]::GetFullPath($PublishPath)
    if (-not (Test-Path -LiteralPath $publishFull -PathType Container)) { throw "Publish path '$publishFull' does not exist." }
    if (-not (Test-Path -LiteralPath (Join-Path $publishFull 'SecureQrPortal.dll') -PathType Leaf)) { throw 'Publish payload does not contain SecureQrPortal.dll.' }
    Assert-NoFirebaseCredentialInPublish -PublishPath $publishFull

    $stageRoot = Join-Path $env:ProgramData 'DA-Secure\staging'
    $stagePath = Join-Path $stageRoot ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stagePath -Force | Out-Null
    $backupPath = $null

    try {
        Write-Step 'Preparing deployment staging directory.'
        Invoke-RobocopyChecked -Source $publishFull -Destination $stagePath
        Copy-PreservedConfiguration -TargetPath $TargetPath -StagePath $stagePath
        Set-StdoutDiagnostics -StagePath $stagePath -Enable:$EnableStdoutLog

        Stop-AppPoolSafely -AppPoolName $AppPoolName
        $backupPath = New-DeploymentBackup -TargetPath $TargetPath -BackupRoot $BackupRoot

        Write-Step 'Deploying staged payload. App_Data is explicitly excluded from mirroring/deletion.'
        New-Item -ItemType Directory -Path $TargetPath -Force | Out-Null
        Invoke-RobocopyChecked -Source $stagePath -Destination $TargetPath -ExtraArguments @('/XD',(Join-Path $TargetPath 'App_Data'))
        Set-DeploymentPermissions -TargetPath $TargetPath -AppPoolName $AppPoolName
        Start-AppPoolSafely -AppPoolName $AppPoolName
        return Wait-ForHealth -HealthUrl $HealthUrl -RequireFirebaseReady:$RequireFirebaseReady
    } catch {
        $deploymentError = $_
        Write-Warning "Deployment health/startup failed: $($deploymentError.Exception.Message)"
        if ($backupPath) {
            Write-Warning 'Automatic rollback is starting.'
            try {
                Restore-DeploymentBackup -BackupPath $backupPath -TargetPath $TargetPath -AppPoolName $AppPoolName -HealthUrl $HealthUrl -RequireFirebaseReady:$RequireFirebaseReady | Out-Null
                Write-Warning 'Automatic rollback completed and previous deployment is healthy.'
            } catch {
                Write-Error "Automatic rollback also failed: $($_.Exception.Message)"
            }
        } else {
            Stop-AppPoolSafely -AppPoolName $AppPoolName
            Write-Warning 'No previous deployment backup existed; application pool was stopped.'
        }
        throw $deploymentError
    } finally {
        Remove-Item -LiteralPath $stagePath -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Get-LatestDeploymentBackup {
    param([Parameter(Mandatory)][string]$BackupRoot)
    if (-not (Test-Path -LiteralPath $BackupRoot -PathType Container)) { return $null }
    return Get-ChildItem -LiteralPath $BackupRoot -Directory -Filter 'deploy-*' |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
}
