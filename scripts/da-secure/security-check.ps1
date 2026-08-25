[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $repoRoot
try {
    Write-Host '== DA Secure static security regression =='

    function Assert-NoGitPattern {
        param(
            [Parameter(Mandatory=$true)][string]$Pattern,
            [Parameter(Mandatory=$true)][string[]]$Paths,
            [Parameter(Mandatory=$true)][string]$Code
        )
        # -e is mandatory here because security signatures such as PEM headers
        # begin with '-' and must never be parsed as command-line options.
        $args = @('grep','-I','-l','-E','-e',$Pattern,'--') + $Paths
        $matches = & git @args 2>$null
        if ($LASTEXITCODE -eq 0 -and $matches) {
            $files = @($matches | ForEach-Object { $_.Trim() } | Where-Object { $_ })
            throw "$Code detected in production source: $($files -join ', '). Secret/payload values suppressed."
        }
        if ($LASTEXITCODE -notin @(0,1)) { throw "git grep failed for $Code." }
    }

    $productionPaths = @('mobile/da_secure/lib','src/SecureQrPortal')
    Assert-NoGitPattern '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----' $productionPaths 'PRIVATE_KEY'
    Assert-NoGitPattern '"private_key"[[:space:]]*:' $productionPaths 'FIREBASE_ADMIN_PRIVATE_KEY'
    Assert-NoGitPattern '"type"[[:space:]]*:[[:space:]]*"service_account"' $productionPaths 'FIREBASE_SERVICE_ACCOUNT_JSON'
    Assert-NoGitPattern 'badCertificateCallback[[:space:]]*[:=]' @('mobile/da_secure/lib') 'TRUST_ALL_CERT_CALLBACK'
    Assert-NoGitPattern 'HttpOverrides' @('mobile/da_secure/lib') 'HTTP_OVERRIDES'
    Assert-NoGitPattern 'ServerCertificateCustomValidationCallback.*(=>|return)[[:space:]]*true' @('src/SecureQrPortal') 'TRUST_ALL_SERVER_CERT_CALLBACK'
    Assert-NoGitPattern 'Bearer[[:space:]]+eyJ[A-Za-z0-9_-]+' $productionPaths 'HARDCODED_BEARER_TOKEN'
    Assert-NoGitPattern '(refreshToken|accessToken)[[:space:]]*[:=][[:space:]]*["''][A-Za-z0-9._-]{20,}["'']' $productionPaths 'HARDCODED_SESSION_TOKEN'
    Assert-NoGitPattern '(otp|Otp|OTP)[A-Za-z0-9_]*[[:space:]]*(==|=)[[:space:]]*["''][0-9]{6}["'']' $productionPaths 'HARDCODED_OTP'

    $apiConfig = Join-Path $repoRoot 'mobile/da_secure/lib/config/app_config.dart'
    if (Test-Path $apiConfig) {
        $config = Get-Content $apiConfig -Raw
        if ($config -match 'http://') { throw 'TLS REGRESSION: HTTP fallback found in app config.' }
        if ($config -notmatch 'https://testapi\.da\.gov\.kw') {
            throw 'TLS REGRESSION: canonical HTTPS API is not present in centralized app config.'
        }
    }

    $pushSource = Join-Path $repoRoot 'src/SecureQrPortal/Services/FirebaseMobilePushService.cs'
    if (Test-Path $pushSource) {
        $source = Get-Content $pushSource -Raw
        $keys = [regex]::Matches($source, '\["([^"\r\n]+)"\]\s*=') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
        $allowed = @('deliveryId','category','notificationCategory','version')
        $unexpected = @($keys | Where-Object { $_ -notin $allowed })
        if ($unexpected.Count -gt 0) {
            throw "FCM PAYLOAD REGRESSION: unapproved data key(s): $($unexpected -join ', ')."
        }
        $forbiddenWords = @('otp','password','content','html','attachment','bearer','refresh','username','qrToken','shareToken')
        foreach ($word in $forbiddenWords) {
            if ($keys -contains $word) { throw "FCM PAYLOAD REGRESSION: protected key '$word' detected." }
        }
        if ($keys -notcontains 'deliveryId' -or $keys -notcontains 'version') {
            throw 'FCM PAYLOAD REGRESSION: routing metadata is incomplete.'
        }
    }

    $requiredRegressionSuites = @(
        'tests/SecureQrPortal.Tests/CaptchaLoginSecurityTests.cs',
        'tests/SecureQrPortal.Tests/CaptchaServiceTests.cs',
        'tests/SecureQrPortal.Tests/MobileSecurityTests.cs',
        'tests/SecureQrPortal.Tests/MobileSessionControllerTests.cs',
        'tests/SecureQrPortal.Tests/MobileTenantBoundaryTests.cs',
        'tests/SecureQrPortal.Tests/CounterTests.cs',
        'tests/SecureQrPortal.Tests/FirebaseReminderWorkerTests.cs'
    )
    $missingSuites = @($requiredRegressionSuites | Where-Object { -not (Test-Path (Join-Path $repoRoot $_)) })
    if ($missingSuites.Count -gt 0) {
        throw "SECURITY REGRESSION: required test suite(s) missing: $($missingSuites -join ', ')."
    }

    Write-Host 'SECRET SCAN: PASS'
    Write-Host 'TLS STATIC REGRESSION: PASS'
    Write-Host 'PAYLOAD REGRESSION: PASS'
    Write-Host 'SECURITY REGRESSION SUITE PRESENCE: PASS'
}
finally {
    Pop-Location
}
