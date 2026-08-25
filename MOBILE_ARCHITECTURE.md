# DA Secure — Mobile / Backend / Dashboard Architecture

## System boundary

DA Secure extends the existing Secure QR Portal on the isolated mobile branch; it is not a separate unrelated backend.

Authoritative chain:

`Admin Dashboard → ASP.NET Core → EF Core Database → Mobile Delivery Service → Firebase Cloud Messaging → Android DA Secure → Mobile Auth → Organization Inbox → Existing Secure-Page Credentials → Secure Reveal → Counters/Audit → Reminder Engine`

## Existing backend components to reuse

- `Organization`: ownership anchor. Add normalized `MobileNumber` visibly and persistently; do not rely on a Flutter-supplied organization id.
- `SecurePage`: authoritative message source, validity/expiry/revocation and access counters.
- `PageCredential`: existing secure username/password hash. Mobile uses these same credentials; do not create mobile-only QR credentials.
- `SecurePageAccessService`: existing secure credential validation/access-policy/counter/logging behavior. Extend/reuse rather than duplicate business rules.
- Rich Text Editor: `ContentArabicHtml` / `ContentEnglishHtml`; sanitized server content is authoritative message body.
- `SmsGatewayService`: provider abstraction available for OTP delivery when configured. Do not fake success if provider credentials are absent/fail.
- `AuditService`, `AuditLog`, `AccessLog`: extend lifecycle auditing without logging secrets.
- ASP.NET Core Identity admin login: preserve password/lockout/anti-forgery security; add first-party CAPTCHA defense-in-depth.

## Proposed mobile persistence

Add explicit mobile entities/migrations under Worker 2/3 ownership, minimally:

### MobileDevice

- Id
- DeviceId (opaque client/device registration id)
- OrganizationId
- FcmToken (protected appropriately at rest where feasible)
- Platform
- AppVersion
- PushEnabled
- RegisteredAtUtc
- LastSeenAtUtc
- DeactivatedAtUtc nullable
- token/version concurrency fields as needed

### MobileOtpChallenge

- Id / ChallengeId
- OrganizationId
- normalized mobile binding
- OTP secure hash/HMAC only
- ExpiresAtUtc
- AttemptCount / MaxAttempts
- ResendAvailableAtUtc
- ConsumedAtUtc
- CreatedAtUtc
- audit-safe provider result metadata

Never persist or log plaintext OTP.

### MobileSession / refresh-token state

Use server-issued authenticated mobile session credentials with rotation/revocation appropriate for an API client. Persist only safe token hashes/identifiers where persistence is required. Organization identity comes from the authenticated session.

### MobileDelivery

- Id
- SecurePageId
- OrganizationId
- Created/Sent timestamps
- DeliveryStatus
- FirebaseStatus / safe provider message id if available
- RevealLimit snapshot/config only if product rules require delivery-specific limits; otherwise reference authoritative SecurePage access policy deliberately
- RevealCount / FirstRevealedAtUtc according to the selected server-authoritative design
- ExpiresAtUtc
- ReminderEnabled
- ReminderInterval
- ReminderUnit
- NextReminderAtUtc
- ReminderCount
- LastReminderAtUtc
- RevokedAtUtc nullable
- optimistic concurrency/idempotency key

The implementation must explicitly reconcile delivery reveal count with the existing `SecurePage.CurrentSuccessfulAccessCount` policy so there is only one authoritative rule. Do not create conflicting counters.

### MobileDeliveryAudit

May reuse generic `AuditLog` if it supports querying/display requirements cleanly, or use a dedicated entity if lifecycle volume/fields require it. At minimum record lifecycle event, delivery id, timestamp, safe status/result, and actor/source. Never record OTP, passwords, raw tokens, FCM secrets or bearer tokens.

## Flutter architecture

`SCREEN → STATE/USE CASE → REPOSITORY → API CLIENT → https://testapi.da.gov.kw`

Required source areas:

- `config`: centralized base URL/build config
- `networking`: timeouts, JSON, auth header, 401/refresh, connectivity/error mapping, safe retry
- `authentication`: OTP/session/device registration
- `firebase`: messaging initialization/token rotation/push routing
- `routing`: auth-aware routes and pending-delivery resume
- `security`: secure storage and optional local biometric gate
- `localization`: Arabic RTL first and English LTR
- `features`: splash/auth/inbox/secure delivery
- `design_system`: approved deep navy/gold/white tokens/components

No arbitrary HTTP calls inside widgets.

## Push/deep-link behavior

FCM payload contains only safe opaque routing metadata such as `deliveryId`.

Authenticated:

`Push → DA Secure → exact delivery → Secure Login → Secure Message`

Unauthenticated:

`Push → Mobile Auth → OTP → optional biometric prompt → resume pending delivery → Secure Login → Secure Message`

Do not lose the pending delivery destination.

## Reveal semantics

Do not consume a reveal for push send/delivery/tap, app launch, OTP login, inbox open, card selection, secure-login form display, or wrong credentials.

Consume exactly once only after correct existing secure-page username/password and successful server-authorized message reveal.

## Reminder architecture

A server-side durable worker queries due `MobileDelivery` records, checks enabled/not revealed/not revoked/not expired/source valid, obtains eligible registered devices, sends FCM, persists real provider result, increments reminder count, calculates next due time, and prevents concurrent duplicates.

Immediately after first successful reveal, future reminders stop.

## Dashboard integration plan

Visible admin additions:

- Organization registered mobile number
- Registered device status
- Send To App
- reveal limit / remaining reveals
- expiry
- reminder enabled / interval / unit
- delivery status
- Firebase status
- sent time
- unread/opened state
- last reminder / next reminder / reminder count
- first revealed time
- mobile delivery audit/history

No backend-only administrator capability counts as complete.

## First-party CAPTCHA architecture

Create a server-side `CaptchaService` or equivalent with cryptographically random human-readable challenge plus cryptographically random ChallengeId. Store only a secure representation (HMAC/hash) of normalized answer with expiry/attempt state in protected server-side storage/cache/database. Render PNG/SVG without embedding answer in HTML/JS/query/cookie/logs.

Login flow:

`GET Login → issue challenge → render image/id → POST anti-forgery + email + password + challenge id + answer → validate CAPTCHA → validate Identity credentials → sign in → invalidate challenge`.

Refresh invalidates old challenge. Failed CAPTCHA never authenticates. Credential failure issues a new CAPTCHA. Successful login invalidates it. Apply rate limiting and generic feedback.
