[CmdletBinding()]
param(
    [string]$BaseUrl = 'https://testapi.da.gov.kw'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$uri = [Uri]$BaseUrl
if ($uri.Scheme -ne 'https') { throw 'API SMOKE: FAIL - HTTPS is mandatory.' }
if ($uri.Host -ne 'testapi.da.gov.kw') { throw "API SMOKE: FAIL - unexpected host $($uri.Host)." }

function Invoke-SafeRequest {
    param(
        [Parameter(Mandatory=$true)][string]$Method,
        [Parameter(Mandatory=$true)][string]$Path,
        [string]$Body
    )
    $target = [Uri]::new($uri, $Path)
    $params = @{
        Uri = $target
        Method = $Method
        MaximumRedirection = 0
        SkipHttpErrorCheck = $true
        TimeoutSec = 15
        Headers = @{ 'User-Agent' = 'DA-Secure-Release-Smoke/1.0' }
    }
    if ($PSBoundParameters.ContainsKey('Body')) {
        $params.ContentType = 'application/json'
        $params.Body = $Body
    }
    return Invoke-WebRequest @params
}

Write-Host '== DA Secure canonical API safe smoke =='

# TLS validation is intentionally the PowerShell/default platform behavior.
# No SkipCertificateCheck/trust-all option is used.
$root = Invoke-SafeRequest -Method 'GET' -Path '/'
if ($root.StatusCode -ge 500) { throw "API SMOKE: FAIL - root returned HTTP $($root.StatusCode)." }
Write-Host "TLS/HOST: PASS (root HTTP $($root.StatusCode))"

$me = Invoke-SafeRequest -Method 'GET' -Path '/api/mobile/me'
if ($me.StatusCode -notin @(401,403)) {
    throw "AUTH BOUNDARY: FAIL - unauthenticated /api/mobile/me returned HTTP $($me.StatusCode), expected 401/403."
}
Write-Host "AUTH BOUNDARY: PASS (HTTP $($me.StatusCode))"

# Intentionally malformed/empty mobile request. It cannot identify a real organization
# and therefore must not result in a legitimate OTP send.
$otpRequest = Invoke-SafeRequest -Method 'POST' -Path '/api/mobile/auth/request-otp' -Body '{"mobileNumber":""}'
if ($otpRequest.StatusCode -ge 500) {
    throw "MALFORMED REQUEST: FAIL - request-otp returned HTTP $($otpRequest.StatusCode)."
}
if ($otpRequest.StatusCode -in @(200,201,202)) {
    throw "MALFORMED REQUEST: FAIL - empty mobile request was accepted with HTTP $($otpRequest.StatusCode)."
}
if ($otpRequest.StatusCode -notin @(400,401,403,404,409,422,429)) {
    throw "MALFORMED REQUEST: FAIL - unexpected HTTP $($otpRequest.StatusCode)."
}
Write-Host "MALFORMED REQUEST REJECTION: PASS (HTTP $($otpRequest.StatusCode))"

Write-Host 'CANONICAL API SMOKE: PASS'
