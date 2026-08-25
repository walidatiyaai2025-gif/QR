[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$androidRoot = Join-Path $repoRoot 'mobile/da_secure/android'
$appRoot = Join-Path $androidRoot 'app'

$required = @(
    (Join-Path $androidRoot 'gradlew'),
    (Join-Path $androidRoot 'gradlew.bat'),
    (Join-Path $androidRoot 'gradle/wrapper/gradle-wrapper.properties'),
    (Join-Path $androidRoot 'gradle/wrapper/gradle-wrapper.jar'),
    (Join-Path $androidRoot 'settings.gradle.kts'),
    (Join-Path $androidRoot 'build.gradle.kts'),
    (Join-Path $appRoot 'build.gradle.kts'),
    (Join-Path $appRoot 'src/main/AndroidManifest.xml'),
    (Join-Path $appRoot 'google-services.json')
)

$missing = @($required | Where-Object { -not (Test-Path $_) })
if ($missing.Count -gt 0) {
    $relative = $missing | ForEach-Object { $_.Substring($repoRoot.Length).TrimStart('\','/') }
    throw "ANDROID PROJECT AUDIT: FAIL - missing required project files: $($relative -join ', ')"
}

$appGradle = Get-Content (Join-Path $appRoot 'build.gradle.kts') -Raw
if ($appGradle -notmatch 'applicationId\s*=\s*"com\.qr\.mobile\.da"') {
    throw 'PACKAGE ID: FAIL - expected com.qr.mobile.da.'
}

$manifest = Get-Content (Join-Path $appRoot 'src/main/AndroidManifest.xml') -Raw
if ($manifest -notmatch 'android:label="DA Secure"') {
    throw 'APP LABEL: FAIL - expected DA Secure.'
}

$firebasePath = Join-Path $appRoot 'google-services.json'
$firebaseRaw = Get-Content $firebasePath -Raw
if ($firebaseRaw -match '"private_key"\s*:|"type"\s*:\s*"service_account"') {
    throw 'FIREBASE CLIENT CONFIG: FAIL - server credential material must not be committed.'
}
$firebase = $firebaseRaw | ConvertFrom-Json
$packageMatches = @($firebase.client | Where-Object {
    $_.client_info.android_client_info.package_name -eq 'com.qr.mobile.da'
})
if ($packageMatches.Count -ne 1) {
    throw 'FIREBASE CLIENT CONFIG: FAIL - expected exactly one DA Secure Android client.'
}
if ([string]::IsNullOrWhiteSpace([string]$firebase.project_info.project_id)) {
    throw 'FIREBASE CLIENT CONFIG: FAIL - project id missing.'
}

Write-Host 'PACKAGE ID: PASS'
Write-Host 'APP LABEL: PASS'
Write-Host 'FIREBASE CLIENT CONFIG: PASS'
Write-Host 'ANDROID PROJECT AUDIT: PASS'
