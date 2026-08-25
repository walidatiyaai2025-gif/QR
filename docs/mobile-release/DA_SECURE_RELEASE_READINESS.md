# DA Secure — Release Readiness

Status vocabulary: **PASS / FAIL / UNVERIFIED / BLOCKED / WAITING / NOT APPLICABLE**.

This document reports release evidence only. It never promotes source presence or automated tests into live external verification.

## Candidate provenance

- Release harness branch: `worker/da-secure-release-qa-harness`
- Canonical integration branch: `feat/secure-qr-mobile-app-isolated`
- Canonical API: `https://testapi.da.gov.kw`
- Android package: `com.qr.mobile.da`
- App label: `DA Secure`
- Release gate: .NET `10.0.400`, Flutter `3.47.1`, Java `17`

## Evidence categories

| Category | Factual meaning |
|---|---|
| IMPLEMENTED | Source/configuration exists on the candidate. |
| AUTOMATED VERIFIED | Deterministic automated test/check passed on a cited candidate. |
| EXACT-HEAD CI VERIFIED | Workflow checked out the exact candidate SHA and completed the relevant gate. |
| RUNTIME VERIFIED | Code/toolchain executed successfully, without implying external-provider/device success. |
| LIVE EXTERNAL VERIFIED | Real provider/device outcome was observed with exact provenance. |

## Latest completed evidence before this report update

Release workflow run `32909858702` checked out SHA `91882dca5de110f07f2667735a980b2c2c1a0384` and established:

- Backend Release build: PASS, 0 errors.
- Backend tests: PASS, 111 passed / 0 failed / 0 skipped.
- Static Security Regression Gate: PASS.
- Flutter format: PASS.
- Flutter analyze: PASS.
- Flutter tests: PASS.
- Debug APK build command: PASS.
- APK metadata/upload: NOT VERIFIED in that run because a release-workflow step-id expression defect skipped both steps after a successful build. The harness change following that run fixes the QA workflow and requires a new exact-head run before APK artifact PASS can be claimed.
- Android source audit: FAIL because committed Gradle wrapper files are missing. Package id, app label, Java 17 source configuration and Firebase client configuration are separately checked.
- Canonical API safe smoke: BLOCKED by bounded request timeout from the GitHub-hosted runner.

## Active runtime candidates observed separately

- `worker/da-secure-flutter-real-runtime` / PR #16 remains owned by the Flutter runtime worker. Its exact-head worker CI has reported refresh/session test failures; release QA does not modify that production implementation.
- `worker/da-secure-firebase-reminders` / PR #17 remains owned by Firebase/reminder worker. Release static gates accept the canonical reconciled provider/test suite names without duplicating that production implementation.

## Live external evidence

| Dependency | Status | Required evidence not yet present |
|---|---|---|
| Live SMS | UNVERIFIED | Real configured provider + actual received OTP tied to exact runtime provenance. |
| Live FCM | UNVERIFIED | Real Firebase server credentials + registered real Android device + provider acceptance + actual notification receipt. |
| Canonical API from release runner | BLOCKED | Successful bounded HTTPS response with normal certificate validation. |
| Official Al Diwan crest | BLOCKED | Owner-approved binary crest proven on exact mobile candidate; demo SVG is not accepted. |
| Manual visual QA | UNVERIFIED | Exact-SHA screenshots at required widths for Arabic RTL and English LTR. |

## Release decision

Release/QA infrastructure can be READY-FOR-INTEGRATION when its exact-head gates behave truthfully, including truthful failures for production/external blockers. Final product release remains blocked until runtime convergence, Android source reproducibility, canonical API availability, required live SMS/FCM/device evidence, and official visual evidence are closed.

This branch is never authorized to merge mobile work into `main`; no auto-merge and no self-merge.
