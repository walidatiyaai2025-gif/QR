# DA Secure — QA Matrix

A feature is `VERIFIED COMPLETE` only when all applicable gates pass with evidence from the exact tested SHA.

## Definition-of-done gates

| Gate | Required |
|---|---|
| Database/persistence | PASS |
| Service/business logic | PASS |
| API | PASS |
| Authorization | PASS |
| Visible UI | PASS |
| Reachable navigation | PASS |
| Real data | PASS |
| Loading state | PASS |
| Empty state | PASS |
| Error state | PASS |
| Retry state where applicable | PASS |
| Audit | PASS |
| Arabic RTL | PASS |
| English LTR | PASS |
| Automated tests | PASS |
| End-to-end | PASS |
| Visual evidence | PASS |

## Mandatory v0.1 E2E

1. Organization has registered mobile.
2. DA Secure Android app installed.
3. Device registered with FCM token.
4. Admin creates secure Text Editor content.
5. No attachment.
6. Configure reveal count.
7. Configure expiry.
8. Enable/configure reminder.
9. Click `إرسال إلى التطبيق`.
10. Real Firebase push arrives with exact fixed Arabic text.
11. Tap push.
12. Complete mobile-number OTP if unauthenticated.
13. Biometric enrollment is offered; `ليس الآن` works.
14. Correct organization Inbox opens.
15. Other organizations are inaccessible.
16. Opening card does not consume reveal.
17. Secure login appears.
18. Wrong secure credentials do not consume reveal.
19. Correct credentials reveal exact sanitized Text Editor body.
20. Counter increments once.
21. Dashboard shows authoritative updated count/state.
22. Audit shows lifecycle.
23. Zero attachments were required.

## Reminder QA

- Send another delivery and leave unrevealed.
- Wait configured server-side interval.
- Real reminder push arrives.
- Audit/provider result recorded.
- Repeated reminder follows configured policy without concurrent duplicate send.
- Successfully reveal message.
- Future reminders stop immediately and scheduler does not send again.
- Expired/revoked/disabled deliveries never receive reminders.

## Security QA

- Organization A cannot fetch Organization B delivery.
- Modified deliveryId/organizationId/payload cannot bypass authorization.
- Wrong secure credentials do not consume reveal.
- Reveal limit cannot be bypassed.
- Expired/revoked delivery cannot reveal.
- OTP cannot be replayed.
- OTP is absent from logs.
- CAPTCHA cannot be replayed.
- CAPTCHA answer is absent from HTML/JS/query/cookie/logs.
- CAPTCHA refresh invalidates old challenge.
- expired CAPTCHA rejects login.
- correct CAPTCHA + wrong password rejects and issues a new challenge.
- correct CAPTCHA + correct credentials logs in and invalidates challenge.
- no secure passwords/raw tokens/FCM secrets in logs.
- FCM payload contains no sensitive content.
- TLS verification remains enabled.

### Secure Message encryption control QA

- Production defaults persist `SecureMessageEncryption.Enabled=true` and `SecureMessageEncryption.AllowReveal=true`.
- Missing/corrupt encryption policy fails secure and never permits plaintext fallback.
- Missing/corrupt reveal policy fails closed.
- Flutter/browser request models expose no encryption-mode override.
- `Enabled=true` persists authenticated ciphertext, not Text Editor plaintext.
- Ciphertext tampering fails authenticated decryption.
- `Enabled=false` blocks new/replacement Secure Message body writes.
- A blocked replacement leaves the previous ciphertext unchanged.
- Existing encrypted messages remain encrypted while creation is disabled.
- `AllowReveal=false` blocks browser and mobile decryption globally.
- Blocking reveal does not consume the mobile reveal authorization or destroy message keys.
- Re-enabling reveal restores normal authorized reveal for still-valid messages.
- Security settings UI is reachable only under Administrator access.
- Disabling creation requires exact `DISABLE` confirmation.
- Blocking reveal requires exact `BLOCK-REVEAL` confirmation.
- Every actual transition creates exactly one required audit event with administrator identity, UTC timestamp, previous/new value and authoritative client IP.
- Security-setting audit entries contain no message body, ciphertext value, encryption key, password, OTP, bearer token or reveal token.
- Expiry/revocation cryptographically destroys the wrapped message key while retaining ciphertext.
- Temporary reveal shutdown does not trigger key destruction.
- Trusted reverse-proxy handling is verified when a proxy is present; arbitrary `X-Forwarded-For` is not accepted as authoritative.
- Multi-node/active-passive deployments prove all potential active nodes use the same durable Data Protection key ring.

## Screen-state QA

Relevant screens must prove Loading / Empty / Success / Error / Retry. Secure delivery additionally proves Expired / Revoked / Limit Reached / Authentication Failure / Reveal Blocked / Crypto Unavailable where applicable.

## Visual QA

Widths: 360 / 375 / 390 / 412 / 430.

For Arabic and English verify launcher identity, splash, phone login, OTP, biometrics, Inbox, Secure Login, Secure Message, navy/gold/white palette, typography, spacing, cards and bottom navigation.

Admin visual QA must additionally verify `Settings → Security` in Arabic and English, readable ACTIVE/DISABLED and ACTIVE/BLOCKED state indicators, confirmation guidance, responsive layout, and no secret cryptographic material in the UI.

Screenshots from a different SHA are non-authoritative.
