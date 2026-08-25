# DA Secure — E2E Release Matrix

Canonical API: `https://testapi.da.gov.kw`  
Android package: `com.qr.mobile.da`  
Release harness branch: `worker/da-secure-release-qa-harness`

Status vocabulary: **PASS / FAIL / UNVERIFIED / BLOCKED / WAITING FOR CONVERGENCE**.

This matrix separates repository/unit evidence from live external evidence. Automated component tests do not prove live SMS, live FCM receipt, device biometrics, canonical-host deployment parity, or visual approval.

| # | Flow | Required system | Automated evidence | Manual evidence | Live external dependency | Current evidence |
|---:|---|---|---|---|---|---|
| 1 | App launch | Flutter / Android | Widget/navigation suite | Real-device launch | Android device | WAITING FOR CONVERGENCE |
| 2 | Mobile number | Flutter + mobile auth API | Backend auth tests; mobile UI contracts | Real app entry | Canonical API | WAITING FOR CONVERGENCE |
| 3 | OTP request | Mobile auth + SMS gateway | Backend OTP/rate-limit tests | Real registered number | SMS provider | AUTOMATED VERIFIED; LIVE SMS UNVERIFIED |
| 4 | OTP verify | Mobile auth/session | Expiry/replay/attempt tests | Real OTP entry | SMS provider | AUTOMATED VERIFIED; LIVE SMS UNVERIFIED |
| 5 | Session restore | Flutter storage + refresh API | Backend refresh/rotation tests | Kill/relaunch device app | Android device | WAITING FOR CONVERGENCE |
| 6 | Optional biometric | Flutter local_auth | UI/navigation tests where present | Enroll/skip/reopen | Android biometric hardware | LIVE DEVICE UNVERIFIED |
| 7 | Device register | Flutter FCM + device API | Backend device/token tests; active FCM branch tests | Real token registration | Firebase client + Android device | WAITING FOR CONVERGENCE |
| 8 | Inbox | Mobile API + Flutter | Tenant/inbox tests; UI empty/success contracts | Real organization inbox | Canonical API | WAITING FOR CONVERGENCE |
| 9 | Delivery details | Mobile API + Flutter | Ownership/metadata tests | Open owned delivery | Canonical API | WAITING FOR CONVERGENCE |
| 10 | Secure login | Existing page credentials + mobile API | Wrong-credential/security tests | Real credential entry | Canonical API | AUTOMATED VERIFIED; E2E UNVERIFIED |
| 11 | Reveal | Mobile delivery access service | Reveal/counter tests | Real protected-message reveal | Canonical API | AUTOMATED VERIFIED; E2E UNVERIFIED |
| 12 | Reveal count | EF/database + secure reveal | Counter/concurrency tests | Dashboard/app comparison | Canonical API + database | AUTOMATED VERIFIED; E2E UNVERIFIED |
| 13 | Dashboard audit | Audit persistence/admin UI | Audit-related backend tests | Visible admin history | Canonical host | AUTOMATED PARTIAL; MANUAL UNVERIFIED |
| 14 | Initial push | Firebase Admin + device | Provider/dispatch tests | Notification receipt | Firebase Admin credentials + Android device | LIVE FCM UNVERIFIED |
| 15 | Push tap | Flutter FCM routing | Active FCM branch contract tests | Tap notification on device | Android device + live FCM | WAITING FOR CONVERGENCE |
| 16 | Unread reminder | Durable reminder persistence | Reminder due/eligibility tests | Leave delivery unrevealed | Database | AUTOMATED VERIFIED |
| 17 | Reminder push | Reminder worker + Firebase | Reminder worker/provider tests | Receive reminder notification | Firebase Admin credentials + Android device | LIVE FCM UNVERIFIED |
| 18 | Reveal stops reminder | Reveal transaction + reminder worker | Reminder/reveal stop tests | Reveal then observe no future reminder | Database + optional live FCM | AUTOMATED VERIFIED; LIVE E2E UNVERIFIED |

## Security release assertions

The release workflow executes the full backend suite plus static release checks. Required regression coverage includes:

- OTP expiry, replay and attempt limits.
- refresh rotation/replay and session revocation.
- organization/tenant isolation and IDOR resistance.
- wrong secure credentials consume zero reveals.
- authoritative reveal limits and concurrency.
- CAPTCHA expiry, single use, refresh invalidation, anti-forgery, rate limiting and Identity lockout.
- durable reminder scheduling/restart/concurrency/stop conditions.
- FCM data payload restricted to opaque routing metadata.
- TLS verification retained; no HTTP fallback or trust-all callback.

## Mobile responsive QA

Automated mobile widget coverage must run at **360 / 375 / 390 / 412 / 430** for Arabic RTL and English LTR where the integrated candidate contains those tests. Exact-SHA screenshots remain a manual QA artifact and are not manufactured by this harness.

## Admin responsive QA

Manual/existing automated coverage must be recorded at **320 / 360 / 375 / 390 / 412 / 430** for:

- QR Details.
- Send To DA Secure.
- delivery history.
- Organization mobile.
- status/actions.

Current state: **MANUAL VISUAL EVIDENCE UNVERIFIED**. The release harness does not redesign admin screens or create fragile pixel goldens.
