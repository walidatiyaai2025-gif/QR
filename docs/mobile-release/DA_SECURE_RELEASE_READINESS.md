# DA Secure — Release Readiness

Status vocabulary: **PASS / FAIL / UNVERIFIED / BLOCKED / WAITING FOR CONVERGENCE / NOT APPLICABLE**.

This document reports release evidence only. It does not promote component/unit coverage into live external verification.

## Candidate provenance

- Release harness branch: `worker/da-secure-release-qa-harness`
- Integration base at branch creation: `7d0cdfaef1a4ce1292c19212f19fb6d96e880a16`
- Canonical integration branch: `feat/secure-qr-mobile-app-isolated`
- Canonical API: `https://testapi.da.gov.kw`
- Android package: `com.qr.mobile.da`
- App label: `DA Secure`
- Pinned release-gate Flutter SDK: `3.47.1`

## Current release evidence

| Gate | State | Evidence / blocker |
|---|---|---|
| Release harness | PASS | Reusable GitHub Actions release-candidate workflow plus local PowerShell verification scripts are committed on the worker branch. |
| Production ownership overlap | PASS | Release branch changes are limited to `.github/workflows`, `scripts/da-secure`, and `docs/mobile-release`; production services/controllers/models and `mobile/da_secure/lib/**` are unchanged. |
| Backend build | PASS | Completed release workflow evidence: Release build succeeded with 0 errors. |
| Backend tests | PASS | Completed release workflow evidence: 111 passed, 0 failed, 0 skipped. |
| Secret scan | PASS | Static gate scans production source for private keys/service-account material and obvious hardcoded OTP/session credentials without logging detected values. |
| TLS static regression | PASS | Canonical Flutter config remains HTTPS; trust-all certificate callback/HTTP override patterns are rejected. |
| FCM payload regression | PASS | Release static gate restricts Firebase data keys to approved routing metadata and rejects protected-data key names. |
| Auth/CAPTCHA/reminder regression suite presence | PASS | Required existing backend regression suites are present and executed by the full backend test gate. |
| Android package id | PASS | Source applicationId is `com.qr.mobile.da`. |
| Android app label | PASS | Manifest label is `DA Secure`. |
| Firebase Android client config | PASS | Client config is structurally present and matches `com.qr.mobile.da`; server service-account material is rejected. |
| Android project audit | FAIL | Source is missing committed `android/gradlew`, `android/gradlew.bat`, `android/gradle/wrapper/gradle-wrapper.properties`, and `gradle-wrapper.jar`. |
| Flutter format | FAIL | Pinned Dart formatter reports current integrated Flutter candidate requires formatting changes. Release harness does not rewrite active-worker production files. |
| Flutter analyze | WAITING FOR CONVERGENCE | Collected independently after the release workflow was updated to continue diagnostic checks after format failure; final exact-head result must be read from CI. |
| Flutter tests | WAITING FOR CONVERGENCE | Final exact-head result must be read from CI. Earlier isolated FCM helper evidence is not promoted to this candidate. |
| Debug APK | FAIL | No approved release-harness APK exists. Prior exact Flutter 3.47.1 build evidence classified Gradle 8.11.1 as below Flutter's required 8.14.0 minimum; source wrapper is also absent. No dependency-validation bypass is used. |
| APK metadata | UNVERIFIED | Cannot inspect package/version/debug label until APK build succeeds. |
| Canonical API smoke | BLOCKED | GitHub-hosted Windows runner timed out contacting `https://testapi.da.gov.kw/` with normal certificate verification; no trust-all behavior is used. |
| Official crest | BLOCKED | No owner-approved official Al Diwan crest binary is proven in the integration tree; the sample SVG is explicitly not accepted as official identity. |
| Live SMS | UNVERIFIED | No exact-SHA real SMS receipt evidence. |
| Live FCM | UNVERIFIED | No exact-SHA evidence package proving runtime Admin credentials, real registered Android token, provider acceptance, and device receipt. |
| SQL Server live application | UNVERIFIED | Source/test migration evidence does not prove application against the target live SQL Server. |

## CI failure ownership

### Android project audit

- WORKFLOW: `DA Secure Release Candidate`
- JOB: `Android Project Audit`
- STEP: `Android package and Firebase audit`
- COMMAND: `./scripts/da-secure/verify-android.ps1`
- ERROR: committed Gradle wrapper files are missing.
- ROOT CAUSE: Android project skeleton/build reproducibility gap.
- CLASSIFICATION: **TOOLCHAIN DEFECT**.
- OWNER: Flutter/Android runtime convergence owner; release worker does not modify application feature code.

### Flutter format

- WORKFLOW: `DA Secure Release Candidate`
- JOB: `Flutter / APK Gate`
- STEP: `Format check`
- COMMAND: `dart format --output=none --set-exit-if-changed lib test`
- ERROR: current integrated Flutter source is not canonical under Dart 3.13.1 / Flutter 3.47.1.
- ROOT CAUSE: active Flutter source formatting drift.
- CLASSIFICATION: **PRODUCTION DEFECT** (source hygiene; no business-logic diagnosis implied).
- OWNER: active Flutter runtime worker.

### Canonical API smoke

- WORKFLOW: `DA Secure Release Candidate`
- JOB: `Canonical API Safe Smoke`
- STEP: `Non-destructive HTTPS smoke`
- COMMAND: `./scripts/da-secure/api-smoke.ps1 -BaseUrl https://testapi.da.gov.kw`
- ERROR: request timed out before a safe HTTP status could be asserted.
- ROOT CAUSE: canonical host was not reachable/responding from the GitHub-hosted runner within the bounded smoke timeout.
- CLASSIFICATION: **EXTERNAL SERVICE BLOCKER**.
- OWNER: canonical-host/network/runtime owner.

## Product release status

**RELEASE HARNESS = VERIFIED** as release/test infrastructure.

**FINAL PRODUCT RELEASE = WAITING FOR CONVERGENCE** because the current integration candidate still has Flutter formatting/toolchain blockers, no generated provenance-qualified APK, canonical API smoke is externally blocked, official crest approval is blocked, and live SMS/FCM evidence is absent.

No mobile work from this branch is authorized for merge into `main`.
