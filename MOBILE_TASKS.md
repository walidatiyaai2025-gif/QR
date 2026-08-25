# DA Secure — Mobile Task Ownership

## Milestone v0.1 vertical slice

`DA Secure Splash → Mobile Number → OTP → Optional Biometric → Organization Inbox → Dashboard Send To App → Firebase Push → Push Tap → Secure Username/Password → Secure Message Reveal → Reveal Counter Update → Dashboard Audit`

Then:

`Unrevealed Delivery → Configured Reminder Interval → Reminder Push → Audit → Successful Reveal → Future Reminders Stop`

The complete milestone must work with **zero attachments**.

## Worker 1 — Flutter UI / Visual

Owns only:

- `mobile/da_secure/` Flutter presentation/design-system/routing surfaces
- official app icon/splash asset integration
- Splash
- Mobile Number
- OTP
- biometric enrollment prompt
- Organization Inbox
- Secure Login
- Secure Message
- bottom navigation
- push navigation presentation/resume destination
- Arabic RTL / English LTR
- approved visual-reference parity

Dependencies: Worker 2 API contracts/state; Worker 3 FCM lifecycle for real push navigation.

Do not implement fake authentication, fake inbox data, or authoritative counters in Flutter.

## Worker 2 — Mobile Auth / APIs

Owns only:

- Organization registered mobile persistence/API authorization path
- OTP request/verify/refresh backend
- mobile session/token architecture
- organization authorization and IDOR prevention
- device registration API surface shared with Worker 3
- `/api/mobile/me`
- inbox/delivery endpoints
- secure credential authenticate/reveal endpoints
- authoritative reveal count behavior
- API tests/security tests

Dependencies: existing `SecurePageAccessService`, `PageCredential`, `SecurePage`, Identity/security conventions, Worker 4 visible organization mobile management.

## Worker 3 — Firebase / Push / Reminder Scheduler

Owns only:

- Firebase Admin/server provider integration
- FCM device token lifecycle
- initial push
- provider result persistence
- reminder scheduling and stop conditions
- idempotency/concurrency
- device push state
- background service/worker required for durable reminders

Dependencies: Worker 2 device/delivery models; Worker 4 dashboard controls/status UI.

Never fabricate FCM server credentials or claim delivery verified without a real received notification.

## Worker 4 — Dashboard Mobile Integration

Owns only:

- visible Organization Mobile Number management
- visible Send To App action
- Reveal Limit / Expiry delivery configuration
- Reminder Enabled / Reminder Interval controls
- registered device state
- delivery/Firebase status
- last/next reminder and reminder count
- first-revealed/opened state
- visible mobile-delivery audit/history

Dependencies: Worker 2/3 persistence/service contracts.

## Worker 5 — Dashboard Login CAPTCHA / Security

Owns only:

- first-party self-hosted CAPTCHA service/model/storage
- Account Login controller integration
- visible Arabic/English CAPTCHA UX
- refresh, expiry, single-use, max attempts
- secure random challenge id and challenge
- rate limiting and generic auth feedback
- CAPTCHA/login security tests

Do not use Google reCAPTCHA, Cloudflare Turnstile, hCaptcha, external APIs, site keys, or secret keys.

## Worker 6 — QA / E2E / Visual / Security Closure

Owns only independent closure evidence:

- real API E2E against `https://testapi.da.gov.kw`
- Firebase delivery QA
- OTP QA
- biometrics QA
- reminder QA
- CAPTCHA QA
- IDOR/security QA
- reveal counters
- Arabic/English
- responsive widths 360/375/390/412/430
- screenshots from exact tested SHA

Worker 6 must not silently repair production code under another worker's ownership; failed gates go back to the responsible owner.

## Dependency order

1. Worker 1 can build visual shell in parallel with Worker 2 API work.
2. Worker 5 can implement CAPTCHA independently in parallel.
3. Worker 4 may add visible organization-mobile fields only when model/API ownership is coordinated with Worker 2.
4. Worker 3 depends on delivery/device persistence contracts from Worker 2 and visible status/config contracts from Worker 4.
5. Worker 6 closes only after the vertical slice is integrated on the isolated branch.

## Forbidden cross-work

- No worker merges to `main`.
- No auto-merge.
- No fake production data.
- No new unrelated product features.
- No weakening tests.
- No silent business-rule redesign.
