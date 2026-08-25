# DA Secure — Mobile Application Constitution

Project: **DA Secure**  
Repository: `walidatiyaai2025-gif/QR`  
Canonical API: `https://testapi.da.gov.kw`  
Android package: `com.qr.mobile.da`

## Non-negotiable branch rule

**MOBILE WORK MUST NEVER BE MERGED INTO MAIN WITHOUT EXPLICIT OWNER APPROVAL.**

The isolated branch is `feat/secure-qr-mobile-app-isolated`. CI success, mergeability, review approval, or green tests are not authorization to merge into `main`. Auto-merge is forbidden.

## No fake completion

A feature is not complete unless it is connected end-to-end from database/backend/API to a visible reachable UI using real data and has real QA evidence.

Backend-only code, dead APIs, hidden routes, mock cards, static production-looking data, disconnected buttons, generated classes, migrations, Firebase classes, or compilation alone do not count as completion.

For a user-facing capability, VERIFIED COMPLETE requires the applicable real chain:

`DATABASE → SERVICE → API → AUTHORIZATION → UI CONSUMER → REACHABLE SCREEN → REAL DATA → USER ACTION → SERVER RESPONSE → SUCCESS/EMPTY/ERROR → AUDIT → TEST → E2E EVIDENCE`.

If any required link is missing, report `PARTIAL`, `BLOCKED`, or `UNVERIFIED`.

## Fixed product identity

- Application name: **DA Secure**
- System brand: **Al Diwan Al Amiri / الديوان الأميري**
- Android package: `com.qr.mobile.da`
- Canonical API: `https://testapi.da.gov.kw`
- Push transport: Firebase Cloud Messaging
- Firebase client configuration: attached `google-services.json`
- App icon and splash identity: official Al Diwan Al Amiri crest
- Visual contract: approved attached DA Secure mobile designs
- Primary palette: deep navy, gold, white
- Arabic RTL first; English LTR supported

Do not substitute KUNA branding, a generic lock/shield, QR icon, letters `DA`, generic Material screens, or unrelated artwork.

## Security invariants

- TLS certificate verification stays enabled. Never trust-all, bypass SSL errors, downgrade to HTTP, or disable certificate checks.
- The authenticated mobile session determines organization ownership. Flutter must never be trusted to supply authoritative `organizationId`.
- Secure-page username/password remains server-authoritative and is not replaced by biometrics.
- Biometrics may only unlock/recover the DA Secure app session and are optional.
- OTPs are server-generated, expiring, one-time, attempt-limited, rate-limited, replay-resistant, and never logged.
- Firebase is transport only. Never put OTP, passwords, message body, raw QR/share tokens, sensitive attachments, or session secrets in push payloads.
- Reveal counters are server-authoritative and increment only after correct secure credentials and successful protected-message reveal.
- Attachments are optional. `TEXT + ZERO ATTACHMENTS` is a first-class scenario.
- Reminder scheduling is durable and server-side; Flutter is not the authority.
- Dashboard CAPTCHA is first-party/self-hosted, has zero external owner configuration, is server-validated, expiring, single-use, refreshable, rate-limited, and never exposes its answer client-side.

## Fixed message heading

There is no administrator-defined message title. Use exactly:

Arabic: `لديك رسالة جديدة اضغط هنا لاستعراض الرسالة`

English: `You have a new message. Tap here to view it.`

The real secure message body is the sanitized content from the existing dashboard Text Editor.

## Worker protocol

Every worker must first read:

1. `MOBILE_APP_CONSTITUTION.md`
2. `MOBILE_CURRENT_STATE.md`
3. `MOBILE_TASKS.md`
4. `MOBILE_ARCHITECTURE.md`
5. `MOBILE_API_CONTRACT.md`
6. `MOBILE_VISUAL_REFERENCE.md`
7. `MOBILE_QA_MATRIX.md`

Workers must not duplicate another worker's active ownership. Exact branch HEAD must be fetched live before edits. Screenshots/evidence from a different SHA are non-authoritative.
