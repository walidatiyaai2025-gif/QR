[CmdletBinding()]
param(
    [ValidateSet('Static', 'Live')]
    [string]$Mode = 'Static',

    [string]$BaseUrl = 'https://testapi.da.gov.kw',

    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedBaseUrl = 'https://testapi.da.gov.kw'
$uri = [Uri]$BaseUrl
if ($BaseUrl.TrimEnd('/') -ne $expectedBaseUrl) {
    throw "Final QA accepts only canonical API $expectedBaseUrl. Received: $BaseUrl"
}
if ($uri.Scheme -ne 'https') { throw 'HTTP fallback is forbidden.' }
if ($uri.Host -ne 'testapi.da.gov.kw') { throw 'Unexpected API host.' }
if (-not [string]::IsNullOrEmpty($uri.UserInfo)) { throw 'Credentials in API URL are forbidden.' }

$forbiddenMarkers = @(
    'http://testapi.da.gov.kw',
    'badCertificateCallback',
    'HttpOverrides.global',
    'allowBadCertificates',
    'dangerousAcceptAnyServerCertificateValidator',
    'SkipCertificateCheck'
)

$scanRoots = @(
    'mobile/da_secure/lib',
    'src/SecureQrPortal'
)

foreach ($root in $scanRoots) {
    if (-not (Test-Path $root)) { throw "Required source root missing: $root" }
    Get-ChildItem $root -Recurse -File -Include *.dart,*.cs,*.json,*.config | ForEach-Object {
        $text = Get-Content $_.FullName -Raw
        foreach ($marker in $forbiddenMarkers) {
            if ($text.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "Forbidden TLS/HTTP bypass marker '$marker' found in $($_.FullName)."
            }
        }
    }
}

$result = [ordered]@{
    timestampUtc = (Get-Date).ToUniversalTime().ToString('o')
    mode = $Mode
    canonicalApi = $expectedBaseUrl
    canonicalHttps = $true
    trustAllBypassDetected = $false
    liveTlsHandshake = 'UNVERIFIED'
    loginRoute = 'UNVERIFIED'
    adminUnauthenticatedRoutes = 'UNVERIFIED'
    liveSms = 'UNVERIFIED'
    liveFcm = 'UNVERIFIED'
    note = 'Static mode does not constitute live SMS, live FCM, physical-device, or authenticated E2E evidence.'
}

if ($Mode -eq 'Live') {
    # HttpClientHandler uses normal platform certificate validation. Deliberately no
    # ServerCertificateCustomValidationCallback, no -SkipCertificateCheck, and no HTTP fallback.
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(15)
    try {
        $login = $client.GetAsync("$expectedBaseUrl/Account/Login").GetAwaiter().GetResult()
        $result.liveTlsHandshake = 'VERIFIED'
        $result.loginRoute = [int]$login.StatusCode

        $adminRoutes = @(
            '/Admin/Dashboard',
            '/Admin/Organizations',
            '/Admin/Qr',
            '/Admin/SecurePages',
            '/Admin/MobileDelivery/History',
            '/Admin/Logs/Audit',
            '/Admin/Settings/General'
        )
        $routeResults = [ordered]@{}
        foreach ($route in $adminRoutes) {
            $response = $client.GetAsync("$expectedBaseUrl$route").GetAwaiter().GetResult()
            $routeResults[$route] = [int]$response.StatusCode
            if ([int]$response.StatusCode -ge 500) {
                throw "Live unauthenticated route $route returned HTTP $([int]$response.StatusCode)."
            }
        }
        $result.adminUnauthenticatedRoutes = $routeResults
    }
    finally {
        $client.Dispose()
        $handler.Dispose()
    }
}

$json = $result | ConvertTo-Json -Depth 6
if ($OutputPath) {
    $parent = Split-Path -Parent $OutputPath
    if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    Set-Content -Path $OutputPath -Value $json -Encoding utf8
}
$json
