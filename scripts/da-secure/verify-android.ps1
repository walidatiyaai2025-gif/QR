[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$androidRoot = Join-Path $repoRoot 'mobile/da_secure/android'
$appRoot = Join-Path $androidRoot 'app'
$failures = [System.Collections.Generic.List[string]]::new()

$projectFiles = @(
    (Join-Path $androidRoot 'settings.gradle.kts'),
    (Join-Path $androidRoot 'build.gradle.kts'),
    (Join-Path $appRoot 'build.gradle.kts'),
    (Join-Path $appRoot 'src/main/AndroidManifest.xml'),
    (Join-Path $appRoot 'google-services.json')
)
$missingProjectFiles = @($projectFiles | Where-Object { -not (Test-Path $_) })
if ($missingProjectFiles.Count -gt 0) {
    $relative = $missingProjectFiles | ForEach-Object { $_.Substring($repoRoot.Length).TrimStart('\','/') }
    $failures.Add("ANDROID_PROJECT_FILES_MISSING: $($relative -join ', ')")
}

$appGradlePath = Join-Path $appRoot 'build.gradle.kts'
if (Test-Path $appGradlePath) {
    $appGradle = Get-Content $appGradlePath -Raw
    if ($appGradle -match 'applicationId\s*=\s*"com\.qr\.mobile\.da"') {
        Write-Host 'PACKAGE ID: PASS'
    }
    else {
        Write-Host 'PACKAGE ID: FAIL'
        $failures.Add('PACKAGE_ID_DRIFT')
    }
}

$manifestPath = Join-Path $appRoot 'src/main/AndroidManifest.xml'
if (Test-Path $manifestPath) {
    $manifest = Get-Content $manifestPath -Raw
    if ($manifest -match 'android:label="DA Secure"') {
        Write-Host 'APP LABEL: PASS'
    }
    else {
        Write-Host 'APP LABEL: FAIL'
        $failures.Add('APP_LABEL_DRIFT')
    }
}

$firebasePath = Join-Path $appRoot 'google-services.json'
if (Test-Path $firebasePath) {
    $firebaseRaw = Get-Content $firebasePath -Raw
    if ($firebaseRaw -match '"private_key"\s*:|"type"\s*:\s*"service_account"') {
        Write-Host 'FIREBASE CLIENT CONFIG: FAIL'
        $failures.Add('FIREBASE_SERVER_CREDENTIAL_COMMITTED')
    }
    else {
        try {
            $firebase = $firebaseRaw | ConvertFrom-Json
            $packageMatches = @($firebase.client | Where-Object {
                $_.client_info.android_client_info.package_name -eq 'com.qr.mobile.da'
            })
            if ($packageMatches.Count -eq 1 -and -not [string]::IsNullOrWhiteSpace([string]$firebase.project_info.project_id)) {
                Write-Host 'FIREBASE CLIENT CONFIG: PASS'
            }
            else {
                Write-Host 'FIREBASE CLIENT CONFIG: FAIL'
                $failures.Add('FIREBASE_CLIENT_PACKAGE_OR_PROJECT_MISMATCH')
            }
        }
        catch {
            Write-Host 'FIREBASE CLIENT CONFIG: FAIL'
            $failures.Add('FIREBASE_CLIENT_JSON_INVALID')
        }
    }
}

$wrapperFiles = @(
    (Join-Path $androidRoot 'gradlew'),
    (Join-Path $androidRoot 'gradlew.bat'),
    (Join-Path $androidRoot 'gradle/wrapper/gradle-wrapper.properties'),
    (Join-Path $androidRoot 'gradle/wrapper/gradle-wrapper.jar')
)
$missingWrapperFiles = @($wrapperFiles | Where-Object { -not (Test-Path $_) })
if ($missingWrapperFiles.Count -gt 0) {
    $relative = $missingWrapperFiles | ForEach-Object { $_.Substring($repoRoot.Length).TrimStart('\','/') }
    Write-Host 'GRADLE WRAPPER: FAIL'
    $failures.Add("GRADLE_WRAPPER_MISSING: $($relative -join ', ')")
}
else {
    $wrapperProperties = Get-Content (Join-Path $androidRoot 'gradle/wrapper/gradle-wrapper.properties') -Raw
    $match = [regex]::Match($wrapperProperties, 'gradle-([0-9]+\.[0-9]+(?:\.[0-9]+)?)-(?:bin|all)\.zip')
    if (-not $match.Success) {
        Write-Host 'GRADLE VERSION: FAIL'
        $failures.Add('GRADLE_WRAPPER_VERSION_UNREADABLE')
    }
    else {
        $gradleVersion = [version]$match.Groups[1].Value
        $minimum = [version]'8.14.0'
        if ($gradleVersion -lt $minimum) {
            Write-Host "GRADLE VERSION: FAIL ($gradleVersion < $minimum)"
            $failures.Add("GRADLE_VERSION_TOO_OLD:$gradleVersion")
        }
        else {
            Write-Host "GRADLE VERSION: PASS ($gradleVersion)"
        }
    }
}

if ($failures.Count -gt 0) {
    throw "ANDROID PROJECT AUDIT: FAIL - $($failures -join ' | ')"
}

Write-Host 'ANDROID PROJECT AUDIT: PASS'
