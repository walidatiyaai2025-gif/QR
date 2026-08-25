# DA Secure — Release Readiness

Status vocabulary: **PASS / FAIL / UNVERIFIED / BLOCKED / WAITING / NOT APPLICABLE**.

This document reports release evidence only. It never promotes source presence or automated tests into runtime or live external verification.

## Candidate provenance

- Release harness branch: `worker/da-secure-release-qa-harness`
- Canonical integration branch: `feat/secure-qr-mobile-app-isolated`
- Canonical API: `https://testapi.da.gov.kw`
- Android package: `com.qr.mobile.da`
- App label: `DA Secure`
- Release toolchain: .NET `10.0.400`, Flutter `3.47.1`, Java `17`

## Evidence categories

| Category | Factual meaning |
|---|---|
| IMPLEMENTED | Source/configuration exists on the candidate. |
| AUTOMATED VERIFIED | Deterministic automated test/check passed on a cited candidate. |
| EXACT-HEAD CI VERIFIED | Workflow checked out the exact candidate SHA and completed the relevant gate. |
| RUNTIME VERIFIED | Code/toolchain executed successfully, without implying external-provider/device success. |
| LIVE EXTERNAL VERIFIED | Real provider/device outcome was observed with exact provenance. |

## Exact-head release-harness evidence

Release workflow run `32910456097` checked out SHA `58c09d76635978e8be81378e3481c8cb9da9bf53` and established:

### Backend

- Restore: PASS.
- Release build: PASS, **0 errors**.
- Release tests: PASS, **111 passed / 0 failed / 0 skipped**.
- Backend TRX artifact: `da-secure-backend-tests-32910456097`.

### Security

- Secret scan: PASS.
- TLS static regression: PASS.
- FCM payload regression: PASS.
- OPENED semantics writer regression: PASS.
- Required security-regression suite presence: PASS.

### Flutter / Android runtime

- Java setup: PASS, Temurin `17.0.20`.
- Flutter setup: PASS, Flutter `3.47.1` / Dart `3.13.1`.
- `flutter pub get`: PASS.
- `dart format --output=none --set-exit-if-changed lib test`: **FAIL**; 12 files would be reformatted.
- `flutter analyze`: **FAIL**; 6 `unnecessary_underscores` lint issues were reported.
- `flutter test`: PASS, **16 passed**.
- `flutter build apk --debug`: **FAIL**. The build reports project Gradle `8.11.1`, while Flutter `3.47.1` requires at least Gradle `8.14.0`.
- APK metadata validation and upload: correctly SKIPPED because the APK build failed. No provenance-qualified APK artifact is claimed.

The release/QA worker does not modify the Flutter production implementation merely to make these gates green.

### Android structural audit

- Package ID `com.qr.mobile.da`: PASS.
- Java 17 source configuration: PASS.
- App label `DA Secure`: PASS.
- Android Firebase client structure: PASS.
- Gradle wrapper reproducibility: **FAIL** because `gradlew`, `gradlew.bat`, `gradle/wrapper/gradle-wrapper.properties`, and `gradle/wrapper/gradle-wrapper.jar` are not committed on this candidate.

### Canonical API smoke

- Non-destructive HTTPS smoke: **BLOCKED**.
- Observed result: bounded request to `https://testapi.da.gov.kw/` ended with `TaskCanceledException` and was classified `EXTERNAL SERVICE BLOCKER`.
- No HTTP downgrade, trust-all certificate override, destructive call, or synthetic PASS is used.

## Active owner candidates observed separately

- `worker/da-secure-flutter-real-runtime` / PR #16 remains owned by the Flutter runtime worker. Its worker CI has unresolved refresh/session behavior failures; release QA does not push into or merge that branch.
- `worker/da-secure-firebase-reminders` / PR #17 remains owned by the Firebase/reminder worker. Release static gates accept its reconciled canonical provider/test-suite names without duplicating production implementation.

## Live external evidence

| Dependency | Status | Required evidence not yet present |
|---|---|---|
| Live SMS | UNVERIFIED | Real configured provider + actual received OTP tied to exact runtime provenance. |
| Live FCM | UNVERIFIED | Real Firebase server credentials + registered real Android device + provider acceptance + actual notification receipt. |
| Canonical API from release runner | BLOCKED | Successful bounded HTTPS response with normal certificate validation. |
| Official Al Diwan crest | BLOCKED | Owner-approved binary crest proven on exact mobile candidate; demo SVG is not accepted. |
| Manual visual QA | UNVERIFIED | Exact-SHA screenshots at required widths for Arabic RTL and English LTR. |

## Release decision

The release/QA harness is suitable for integration when it reports the above failures truthfully; the product candidate itself is **not release-green**. Runtime convergence must close Flutter formatting/analyzer defects, Android Gradle reproducibility/version compatibility, and the active runtime branch defects. Final device E2E must then prove canonical API connectivity, live SMS, live FCM receipt/tap behavior, and approved visual branding.

This worker is never authorized to merge mobile work into `main`; no auto-merge and no self-merge.
