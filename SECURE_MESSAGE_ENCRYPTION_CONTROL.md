# Secure Message Encryption — Server Control Contract

## Status

This document is a mandatory production security contract for Secure Message content.

The backend is the only authority for encryption and reveal policy. Flutter, browser clients, API callers, request payloads, local storage, and client feature flags must never choose or override the encryption mode.

## Authoritative settings

Persist through the existing `ApplicationSettings` / `AppSettingsService` architecture:

- `SecureMessageEncryption.Enabled` — production default `true`.
- `SecureMessageEncryption.AllowReveal` — production default `true`.

Missing or malformed settings fail secure:

- missing/malformed `Enabled` means encryption remains required;
- missing/malformed `AllowReveal` means reveal is blocked.

A settings-store/database read failure must never cause plaintext fallback. The request/application may fail closed instead.

## Write policy

When `Enabled=true`, every new or replacement Secure Message body is sanitized and then stored only as authenticated ciphertext. A fresh random data-encryption key is generated for each message; the key is wrapped by server-side ASP.NET Core Data Protection. Ciphertext is bound to the message identity/language using authenticated associated data.

When `Enabled=false`, creation/replacement is rejected. Disabled never means plaintext storage.

Existing encrypted messages remain encrypted when creation is disabled.

## Legacy data migration

Pre-feature rows with `ContentEncryptionVersion=0` are encrypted during startup after schema migration and before the application starts accepting traffic. A cryptographic inconsistency aborts startup rather than leaving plaintext available.

Do not manually set an encrypted row back to version 0. Do not copy production plaintext into database columns as a migration workaround.

## Reveal policy

`AllowReveal=true` permits the normal already-authorized browser/mobile reveal flow. Authorization, credential checks, organization ownership, expiry/revocation and reveal-count rules remain authoritative and unchanged.

`AllowReveal=false` blocks all Secure Message decryption/reveal globally. It does not destroy wrapped message keys and does not mutate ciphertext. Re-enabling restores normal authorized reveal for messages whose lifecycle has not terminated.

A cryptographic envelope/key failure returns a safe unavailable result and never falls back to database plaintext.

## Key lifecycle

A message data key is cryptographically destroyed by clearing its wrapped key when the owning Secure Page reaches terminal expiry or revocation. Ciphertext remains in storage and becomes undecryptable.

Temporary `AllowReveal=false` is not a terminal lifecycle event and must not destroy keys.

A delivery-specific expiry/revocation must not destroy a Secure Page key while other deliveries of that Secure Page may remain valid.

## Data Protection key-ring durability

The wrapped per-message keys depend on the ASP.NET Core Data Protection key ring. That key ring is production security state and must be durable for the full lifetime of any decryptable Secure Message.

Single-node default:

- `App_Data/keys`

Multi-node / active-passive production:

- configure `Security:DataProtectionKeyRingPath` (or environment variable `Security__DataProtectionKeyRingPath`) to the same secured durable key-ring location accessible by every node that may become active;
- the location may be a protected UNC/shared storage path under infrastructure control;
- grant only the required application identities and administrators access;
- do not copy different independent key rings to each node;
- do not delete/rotate away keys required by still-valid messages;
- if an explicitly configured path is unavailable, startup must fail rather than silently generate an unrelated local key ring.

The same key-ring continuity also protects existing server-protected application state.

## Reverse proxy and authoritative client IP

Security-setting audit entries use `HttpContext.Connection.RemoteIpAddress` only after ASP.NET Core forwarded-header processing.

Only explicitly trusted reverse proxy addresses may be added to `ReverseProxy:KnownProxies` / `ReverseProxy__KnownProxies__N`. Never trust arbitrary client-supplied `X-Forwarded-For` headers.

If there is no trusted reverse proxy, leave `KnownProxies` empty and the direct connection IP remains authoritative.

## Administration

UI location: `Admin → Settings → Security`.

Only the existing `Administrator` role may access or change these settings. No mobile, organization or anonymous endpoint exists for modifying them.

Disabling creation requires exact confirmation `DISABLE`.

Blocking reveal requires exact confirmation `BLOCK-REVEAL`.

Status indicators:

- Encryption: `ACTIVE` / `DISABLED`
- Reveal: `ACTIVE` / `BLOCKED`

The page must not expose message content, encryption keys, wrapped keys, ciphertext values or other secret cryptographic material.

## Audit events

Every actual state transition records one of:

- `SECURE_MESSAGE_ENCRYPTION_ENABLED`
- `SECURE_MESSAGE_ENCRYPTION_DISABLED`
- `SECURE_MESSAGE_REVEAL_ENABLED`
- `SECURE_MESSAGE_REVEAL_DISABLED`

Each entry contains administrator identity, UTC timestamp, previous/new boolean values, action and authoritative client IP.

Never include message plaintext, ciphertext, wrapped/unwrapped keys, passwords, OTPs, bearer tokens or reveal tokens in these audit entries.

A rejected confirmation or no-op setting submission does not create a false state-change audit event.

## Release gate

Before promotion, exact-SHA evidence must prove at minimum:

1. defaults are enabled;
2. missing/corrupt settings fail secure;
3. client models cannot choose encryption mode;
4. enabled writes produce authenticated ciphertext with no plaintext storage;
5. disabled writes do not mutate ciphertext into plaintext;
6. disabled writes are rejected;
7. existing encrypted data remains encrypted;
8. creation disable does not expose old plaintext;
9. reveal block is global;
10. reveal re-enable restores normal authorized access;
11. settings are Administrator-only;
12. unauthorized callers cannot mutate settings;
13. every state transition creates the required audit event;
14. audit data contains no cryptographic/message secrets;
15. tampered ciphertext fails authentication;
16. expiry/revocation destroys only the wrapped message key and preserves ciphertext;
17. multi-node deployments use one durable Data Protection key ring;
18. trusted reverse-proxy client IP handling is configured and verified where a proxy is present.

Do not mark this feature `VERIFIED COMPLETE` until Release build/tests and applicable end-to-end/visual checks pass for the exact candidate SHA.
