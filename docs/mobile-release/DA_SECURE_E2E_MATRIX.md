# DA Secure — E2E Release Matrix

Canonical API: `https://testapi.da.gov.kw`  
Android package: `com.qr.mobile.da`  
Release harness branch: `worker/da-secure-release-qa-harness`

Status vocabulary: **PASS / FAIL / UNVERIFIED / BLOCKED / WAITING**.

Automated component evidence never substitutes for live SMS, live FCM receipt, device biometrics, canonical-host deployment parity, or exact-SHA visual approval.

Latest release-harness evidence before this document update: workflow `32910456097` on SHA `58c09d76635978e8be81378e3481c8cb9da9bf53`. Backend and security gates passed. Flutter tests passed, while format/analyze and APK build failed; Android Gradle wrapper audit failed; canonical API smoke was externally blocked.

| Flow | Automated | Manual | Live dependency | Current evidence | Status |
|---|---|---|---|---|---|
| Splash | Flutter widget/navigation tests | Exact-SHA real-device launch + visual comparison | Android device + official crest | Widget tests exist; final Android runtime/APK and official crest are not ready | BLOCKED |
| Mobile Number | Backend mobile auth + Flutter tests | Real app entry | Canonical API | Automated path exists; current Flutter release gate is not green | WAITING |
| OTP Request | OTP/rate-limit backend tests | Real registered number | SMS provider + canonical API | Automated verification only | UNVERIFIED |
| OTP Verify | OTP expiry/replay/attempt tests | Enter real received OTP | SMS provider + canonical API | Automated verification only | UNVERIFIED |
| Session Restore | Refresh rotation/replay tests + Flutter session tests | Kill/relaunch app | Canonical API + Android device | Backend regression passes; active runtime PR still has refresh/session defects | WAITING |
| Biometric | Flutter local-auth contracts where present | Enroll/skip/reopen | Android biometric hardware | No exact-SHA device evidence | UNVERIFIED |
| Device Register | Backend device/token tests | Register real FCM token | Firebase client + canonical API + device | Backend automated evidence only | WAITING |
| Inbox | Tenant/inbox backend + Flutter tests | Open real organization inbox | Canonical API | Authorization/metadata tests pass; final runtime/API E2E pending | WAITING |
| Delivery Detail | Ownership/metadata tests | Open owned delivery | Canonical API | Automated metadata evidence | WAITING |
| Secure Login | Wrong/correct secure credential tests | Real credential entry | Canonical API | Wrong credentials consume zero reveals; authentication alone is not OPENED | PASS |
| Reveal | Secure reveal/counter tests | Real protected-message reveal | Canonical API | Successful reveal is server-authoritative in automated tests | PASS |
| Reveal Counter | Counter/limit/concurrency tests | Compare app/admin state | Canonical API + database | Automated authoritative counter evidence | PASS |
| Dashboard Audit | Backend audit tests | Visible admin history | Canonical host | Automated audit evidence; manual UI unverified | UNVERIFIED |
| Initial Push | Firebase provider/dispatch tests | Receive real notification | Firebase Admin credentials + registered Android device | Automated provider evidence only; live receipt absent | UNVERIFIED |
| Push Tap | Routing/navigation tests where integrated | Tap real notification | Live FCM + Android device | Tap/navigation is not OPENED; real-device route not proven | WAITING |
| Reminder | Durable reminder tests | Receive real reminder | Firebase Admin credentials + Android device | Due/eligibility/retry/concurrency automated evidence | UNVERIFIED |
| Reveal Stops Reminder | Reveal + scheduler stop tests | Reveal then observe no future push | Database + optional live FCM | Server-side schedule stop is automated; live observation absent | PASS |
| Logout | Session revocation backend + Flutter auth tests | Logout and verify protected route denial | Canonical API + Android device | Automated revocation coverage; final runtime/device E2E pending | WAITING |

## Opened semantics release invariant

The exact-candidate static release gate enforces that `MobileDelivery.FirstRevealedAtUtc` has one authoritative production writer, in `MobileDeliveryAccessService`, after successful secure access registration. The required invariant is:

- Firebase provider acceptance != OPENED.
- push receipt/tap/navigation != OPENED.
- OTP verification/session issuance != OPENED.
- Inbox/details access != OPENED.
- secure-login authentication != OPENED.
- only successful secure reveal sets `FirstRevealedAtUtc` / `REVEALED` and stops future reminders.

## Security release assertions

The release workflow executes the full backend suite plus static release checks covering OTP expiry/replay/attempt limits, refresh rotation/replay/session revocation, tenant/IDOR isolation, secure credential/reveal limits, CAPTCHA single-use/expiry/refresh/anti-forgery/rate limiting/lockout, Firebase safe payload/fail-closed behavior where integrated, reminder durability/concurrency/stop conditions, TLS, secret scanning, and OPENED semantics.

## Responsive and visual QA

Flutter automated rendering tests pass at **360 / 375 / 390 / 412 / 430** for Arabic and English on the inspected integration candidate. This is automated layout evidence, not approved exact-SHA screenshots. Manual visual QA and official crest approval remain required. Admin responsive evidence remains required at **320 / 360 / 375 / 390 / 412 / 430**. This harness does not manufacture screenshots or substitute the demo SVG for the official crest.
