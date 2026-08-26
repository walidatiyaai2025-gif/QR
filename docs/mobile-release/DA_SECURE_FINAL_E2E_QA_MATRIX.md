# DA Secure v0.1 — Final E2E / Security / Device QA Matrix

## Candidate provenance

- Production candidate under test: `e9c62cd8ba215e5f1e038cdddb9776acc70990bb`
- Source branch: `lead/da-secure-runtime-convergence`
- QA branch: `worker/da-secure-final-e2e-device-qa`
- Integration base observed: `feat/secure-qr-mobile-app-isolated` @ `7d0cdfaef1a4ce1292c19212f19fb6d96e880a16`
- Candidate exact-head convergence run: `32912606115`
- Candidate relationship to integration at QA start: 44 commits ahead / 0 behind.

This matrix distinguishes implementation and automated verification from evidence that requires the real IIS environment, SMS provider, Firebase Admin credentials, and a physical Android device. Mocks never satisfy LIVE SMS or LIVE FCM.

## Final matrix

| Area | Status | Evidence / decision |
|---|---|---|
| Backend Release build | VERIFIED | Exact candidate run `32912606115`; .NET 10.0.400; build 0 errors. One pre-existing CS9113 warning only. |
| Backend automated tests | VERIFIED | Exact candidate: 125 passed / 0 failed / 0 skipped before this QA-only branch adds final Admin smoke coverage. |
| Admin major-route HTTP 500 smoke | IMPLEMENTED | Final QA adds authenticated MVC smoke across Dashboard, Organizations, QR, Secure Pages, Mobile Delivery, Logs, Settings in `en` and `ar`. Requires QA-branch CI result before promotion to VERIFIED. |
| Admin Arabic localization quality | BLOCKED | PR #19 reports four P1 localization gaps: Mobile Delivery raw state labels, Organization Edit branding policy, General Settings branding policy/help, and Change Password validation/success/error localization. Owner: `worker/da-secure-admin-hotfix-closure`. |
| Admin English localization quality | IMPLEMENTED | Major surfaces exist; PR #19 reports residual bypasses/raw state presentation. Final route-health smoke is separate from copy-quality closure. |
| Mobile Arabic | VERIFIED | Candidate Flutter suite verifies RTL and 360/375/390/412/430 widths without overflow. |
| Mobile English | VERIFIED | Candidate Flutter suite verifies LTR and 360/375/390/412/430 widths without overflow. |
| Mobile auth / OTP/session | VERIFIED | Automated contracts verify OTP request/verify behavior, server-authorized session, bearer token, logout, refresh single-flight/retry-once, rotation, and failed-refresh cleanup. |
| Tenant from authenticated session | VERIFIED | Request contracts do not accept authoritative `OrganizationId`; tenant-bound services and bearer claims are tested. |
| IDOR denial | VERIFIED | `MobileTenantBoundaryTests` and `MobileSecurityTests` deny cross-organization details/reveal, including an intentionally inconsistent delivery row. |
| OTP rate limiting | VERIFIED | Resend cooldown and OTP max-attempt enforcement are covered by backend tests. |
| Refresh rotation | VERIFIED | Old refresh token replay is rejected after rotation; Flutter runtime also verifies exactly-one refresh/retry and single-flight concurrency. |
| Failed refresh clears reusable session | VERIFIED | Flutter exact candidate contract passes. |
| Secure credential 401 does not invoke mobile refresh | VERIFIED | Dedicated `refresh_scope_contract_test.dart` passes on exact candidate. |
| CAPTCHA first-party / single-use | VERIFIED | Local `SecureQrPortal.Security.Captcha` implementation; tests verify PNG challenge, answer HMAC, replay denial, expiration, refresh invalidation, max attempts, and exactly one concurrent success. |
| Safe FCM routing metadata | VERIFIED | Backend envelope contains only `deliveryId`, `category`, `version`; Flutter behavior rejects malformed IDs, unexpected/sensitive fields, and bad category/version. |
| Credentials/password/secure body absent from FCM | VERIFIED | Backend and Flutter behavioral tests enforce the safe payload allowlist; protected body/secret metadata are rejected. |
| Counter increments only after successful reveal | VERIFIED | Wrong credentials do not consume reveal; auth alone/push/tap do not mark opened; successful reveal consumes exactly one authoritative server-side access. |
| Audit on reveal/reminder lifecycle | VERIFIED | Automated backend tests assert reminder-stop audit on authoritative first secure reveal and safe audit behavior. |
| Reminder idempotency | VERIFIED | Initial push idempotency and concurrent reminder processors result in one provider occurrence. |
| Reminder stops after reveal/revoke/expiry | VERIFIED | Automated stop-condition matrix covers first reveal, revoke, delivery expiry, organization disabled, page disabled and page revoked. |
| Invalid FCM tokens retired | VERIFIED | Invalid-token test disables/deactivates device, clears protected token and avoids raw-token audit leakage. |
| Firebase backend implementation | VERIFIED | Canonical Firebase Admin provider and durable reminder processor are covered by exact candidate backend suite. |
| Firebase provider credentials in live environment | UNVERIFIED | Repository/CI does not provide admissible real credential evidence. Missing credentials fail closed. |
| Live FCM provider acceptance | UNVERIFIED | No admissible real-device provider-acceptance evidence found. |
| Physical notification receipt | UNVERIFIED | Requires a real registered Android device and observed notification. |
| Push tap opens correct live delivery | UNVERIFIED | Requires physical notification receipt and tap observation on the same real delivery. |
| Live SMS | UNVERIFIED | No real SMS provider delivery evidence found. Automated disabled/test gateway behavior is not live evidence. |
| Canonical API configuration | VERIFIED | Mobile default is exactly `https://testapi.da.gov.kw`; final QA adds an exact HTTPS/no-fallback invariant. |
| TLS source policy | VERIFIED | Final QA gate rejects HTTP fallback and common trust-all certificate hooks in production source. |
| Live TLS handshake to canonical API | BLOCKED | QA execution environment could not resolve/retrieve the external host. No `-k`, trust-all, or HTTP downgrade was used. Owner: `worker/da-secure-iis-deployment-closure` / deployment environment. |
| APK build | VERIFIED | Exact candidate run built and uploaded `da-secure-runtime-convergence-debug-apk`, artifact ID `9587142065`. |
| Physical APK/device smoke | UNVERIFIED | No physical Android execution evidence available in repository CI. |
| Full live E2E | BLOCKED | Cannot be VERIFIED until real SMS/OTP provider evidence (where required), Firebase Admin credential, real registered Android device/token, provider acceptance, physical receipt, tap-to-correct-delivery, secure reveal, counter/audit observation, and reminder stop are captured end-to-end. |

## Candidate exact-head evidence already GREEN

Exact candidate workflow run `32912606115`:

- Backend Release: PASS
- Backend tests: 125 / 125 PASS
- Flutter format: PASS (0 changed)
- Flutter analyze: PASS (No issues found)
- Flutter tests: 64 / 64 PASS
- Android identity: PASS (`com.qr.mobile.da`, `DA Secure`, matching Firebase client package)
- Debug APK: PASS
- APK artifact upload: PASS, artifact ID `9587142065`

## Tracked PR status at QA start

| PR | State | Role in final QA |
|---|---|---|
| #16 | OPEN / mergeable | Flutter real-runtime implementation. Head observed `1404a67d215dcd151cb7a85094bcef5d7981594a`. |
| #17 | OPEN / mergeable | Firebase backend/durable reminder implementation. Head `4ab847b76e93e6b92bdaa9758d441c3ec0534c06`; explicitly declares LIVE FCM UNVERIFIED. |
| #18 | OPEN / mergeable | Release/QA/security harness. Head `438a45165cfe5823601af120c61c79d7be73295a`; earlier APK observations are superseded by exact candidate convergence run `32912606115`. |
| #19 | OPEN / mergeable | Localization QA evidence. Head `f0c2228cca2bd91bc0c16cdd72ae2d93ceb18494`; reports 4 P1 localization gaps. |

## Tracked worker branches at QA start

`worker/da-secure-admin-hotfix-closure`, `worker/da-secure-admin-runtime-regression`, and `worker/da-secure-iis-deployment-closure` all resolve to candidate SHA `e9c62cd8ba215e5f1e038cdddb9776acc70990bb`. They therefore have no distinct closure commit beyond the candidate at this checkpoint.

## Release decision

**Release status: BLOCKED FOR FINAL LIVE E2E SIGN-OFF.**

The candidate has strong automated backend/mobile/security/APK evidence. Final sign-off still requires the four P1 Admin localization gaps to be closed and re-verified, plus admissible live environment/device evidence for the external legs. No mock, emulator-only provider stub, source assertion, or CI artifact may be promoted to LIVE FCM/SMS evidence.
