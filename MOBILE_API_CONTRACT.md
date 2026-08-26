# DA Secure — Mobile API Contract

Canonical base URL: **`https://testapi.da.gov.kw`**

Flutter must use one centralized base URL configuration. Localhost/127.0.0.1/10.0.2.2 may be used only for explicit developer testing; authoritative E2E uses the canonical HTTPS host. TLS validation must remain enabled.

## Server-controlled Secure Message encryption

Secure Message encryption is a server policy. Flutter must never send, infer, cache or override an encryption-enabled/disabled flag or encryption mode.

There is no mobile API for changing `SecureMessageEncryption.Enabled` or `SecureMessageEncryption.AllowReveal`.

Client payloads must not contain `encrypt`, `encryptionMode`, `allowReveal` or equivalent policy fields. Any unknown client-supplied field must never influence server cryptographic behavior.

When server-side creation encryption is disabled, administrator write/replace operations fail; the mobile app does not receive plaintext fallback content.

When global reveal is blocked, the reveal endpoint returns a safe localized unavailable error. Re-enabling reveal restores the normal authorized flow for still-valid messages.

## Auth

### POST `/api/mobile/auth/request-otp`

Input: normalized/normalizable mobile number. Server resolves the active Organization registered to that mobile; do not accept authoritative organizationId from the client.

Success response returns an opaque OTP challenge id plus safe expiry/resend metadata. Never return the OTP.

Failure states include invalid mobile, no active registered organization, resend cooldown/rate limit, SMS provider unavailable/failure.

### POST `/api/mobile/auth/verify-otp`

Input: challenge id + OTP. On success issue authenticated mobile session/refresh material and organization-safe profile context. OTP is one-time, expiring, attempt-limited and replay-resistant.

### POST `/api/mobile/auth/refresh`

Rotates/refreshes mobile session according to server policy. Revoked/expired refresh material must fail closed.

### POST `/api/mobile/auth/logout`

Authenticated. Revokes the current mobile bearer session server-side. Subsequent use of that access token or its associated refresh material must fail closed.

## Device

### POST `/api/mobile/devices/register`

Authenticated. Registers/updates DeviceId, FCM token, platform, app version, push-enabled state. OrganizationId is derived from the session. FCM token rotation updates registration.

## Current organization

### GET `/api/mobile/me`

Returns only safe current-organization/app-session information needed by the UI.

## Inbox

### GET `/api/mobile/inbox`

Authenticated. Returns only deliveries belonging to the session organization. Supports real empty state and safe pagination if needed. No custom administrator-defined message title; card heading is fixed client/product copy.

### GET `/api/mobile/inbox/{deliveryId}`

Authenticated. Returns delivery metadata/status only when owned by the session organization. Opening metadata must not consume reveal.

### POST `/api/mobile/inbox/{deliveryId}/authenticate`

Authenticated mobile session plus existing secure-page username/password. Wrong credentials do not consume reveal. Never return password/hash.

Implementation may return a short-lived opaque reveal authorization bound to delivery/session, or combine authenticate/reveal server-side while preserving atomic one-time reveal semantics.

### POST `/api/mobile/inbox/{deliveryId}/reveal`

Server revalidates organization ownership, delivery/page state, expiry/revocation/limits, secure authentication and the global server-side reveal policy. On success the server decrypts the authenticated ciphertext only after those checks and returns the exact sanitized Text Editor body, sent/expiry metadata, remaining reveal count, and attachments array (possibly empty). Counter increments exactly once according to authoritative server policy.

A valid reveal authorization must not be consumed merely because global reveal is temporarily blocked or the cryptographic envelope is safely unavailable.

Valid response must support `attachments: []`.

Additional stable reveal failure codes include:

- `SECURE_MESSAGE_REVEAL_BLOCKED` — global server-side reveal policy is temporarily blocked;
- `SECURE_MESSAGE_CRYPTO_UNAVAILABLE` — encrypted message cannot be safely decrypted; no plaintext fallback is permitted.

## Admin mobile delivery

### POST `/api/admin/mobile-delivery/send`

Authorized administrator only. Input identifies secure page plus visible delivery configuration. Server validates page, organization, registered mobile/device state, expiry/reveal/reminder config, creates delivery, sends via Firebase, stores real provider result and audit, and returns real success/failure. Never fake `sent successfully`.

Additional explicit admin routes may be added for:

- delivery configuration/status
- reminder settings
- delivery revoke
- audit/history

Every API must have a real consumer.

## Error model

Use stable machine-readable code plus localized-safe message fields. Required user-facing cases include:

- `NO_NETWORK` (client-mapped)
- `TIMEOUT` (client-mapped)
- API unavailable
- unauthorized/session expired
- invalid/expired/replayed OTP
- OTP rate limit/cooldown
- delivery not found
- wrong organization / access denied
- expired/revoked delivery
- reveal limit reached
- invalid secure credentials
- global Secure Message reveal blocked
- encrypted Secure Message safely unavailable
- no registered device
- Firebase delivery failure

Do not collapse all failures into a generic message.

## Push payload

Allowed: opaque routing metadata such as `deliveryId` and non-sensitive notification category/version.

Forbidden: OTP, secure username/password, Text Editor body, raw QR/share token, attachment content, bearer/session/refresh secret, Firebase server secret, encryption key material or server encryption/reveal policy controls.
