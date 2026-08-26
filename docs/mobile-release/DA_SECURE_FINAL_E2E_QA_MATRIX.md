# DA Secure v0.1 — Final E2E / Security / Device QA Matrix

## Candidate provenance

- Production candidate validated: `e9c62cd8ba215e5f1e038cdddb9776acc70990bb`
- Source branch observed at QA start: `lead/da-secure-runtime-convergence`
- QA branch: `worker/da-secure-final-e2e-device-qa`
- Integration base observed: `feat/secure-qr-mobile-app-isolated` @ `7d0cdfaef1a4ce1292c19212f19fb6d96e880a16`
- Candidate exact-head convergence run: `32912606115`
- Evidence-bearing final QA checkpoint: `29525857f858c43e5bc26814a70a7dd54bb3cf97`
- Evidence-bearing final QA run: `32914294391`
- Candidate relationship to integration at QA start: 44 commits ahead / 0 behind.

This matrix distinguishes implementation and automated verification from evidence that requires the real IIS environment, SMS provider, Firebase Admin credentials, and a physical Android device. Mocks never satisfy LIVE SMS or LIVE FCM.

## Final matrix

| Area | Status | Evidence / decision |
|---|---|---|
| Backend Release build | VERIFIED | QA run `32914294391`; .NET 10.0.400; Release build 0 errors. One pre-existing CS9113 warning only. |
| Backend automated tests | VERIFIED | QA exact-head checkpoint: 127 passed / 0 failed / 0 skipped. |
| Admin major-route HTTP 500 smoke | VERIFIED | Authenticated real MVC pipeline tested Dashboard, Organizations, QR, Secure Pages, Mobile Delivery, Logs, Settings in both `en/ltr` and `ar/rtl`; all returned HTTP 200 with no HTTP 500. |
| Admin Arabic localization quality | BLOCKED | PR #19 reports four P1 localization gaps: Mobile Delivery raw state labels, Organization Edit branding policy, General Settings branding policy/help, and Change Password validation/success/error localization. Owner: `worker/da-secure-admin-hotfix-closure`. |
| Admin English localization quality | BLOCKED | Route health is verified, but PR #19 reports residual localization bypasses/raw state presentation requiring production-owner closure before localization sign-off. |
| Mobile Arabic | VERIFIED | Flutter suite verifies RTL and 360/375/390/412/430 widths without overflow. |
| Mobile English | VERIFIED | Flutter suite verifies LTR and 360/375/390/412/430 widths without overflow. |
| Mobile auth / OTP/session | VERIFIED | Automated contracts verify OTP request/verify behavior, server-authorized session, bearer token, logout, refresh single-flight/retry-once, rotation, and failed-refresh cleanup. |
| Tenant from authenticated session | VERIFIED | Request contracts do not accept authoritative `OrganizationId`; tenant-bound services and bearer claims are tested. |
| IDOR denial | VERIFIED | `MobileTenantBoundaryTests` and `MobileSecurityTests` deny cross-organization details/reveal, including an intentionally inconsistent delivery row. |
| OTP rate limiting | VERIFIED | Resend cooldown and OTP max-attempt enforcement are covered by backend tests. |
| Refresh rotation | VERIFIED | Old refresh token replay is rejected after rotation; Flutter runtime verifies exactly-one refresh/retry and single-flight concurrency. |
| Failed refresh clears reusable session | VERIFIED | Flutter runtime contract passes. |
| Secure credential 401 does not invoke mobile refresh | VERIFIED | Dedicated `refresh_scope_contract_test.dart` passes. |
| CAPTCHA first-party / single-use | VERIFIED | Local `SecureQrPortal.Security.Captcha` implementation; tests verify PNG challenge, answer HMAC, replay denial, expiration, refresh invalidation, max attempts, and exactly one concurrent success. |
| Safe FCM routing metadata | VERIFIED | Backend envelope contains only `deliveryId`, `category`, `version`; Flutter behavior rejects malformed IDs, unexpected/sensitive fields, and bad category/version. |
| Credentials/password/secure body absent from FCM | VERIFIED | Backend and Flutter behavioral tests enforce the safe payload allowlist; protected body/secret metadata are rejected. |
| Counter increments only after successful reveal | VERIFIED | Wrong credentials do not consume reveal; auth alone/push/tap do not mark opened; successful reveal consumes exactly one authoritative server-side access. |
| Audit on reveal/reminder lifecycle | VERIFIED | Automated backend tests assert reminder-stop audit on authoritative first secure reveal and safe audit behavior. |
| Reminder idempotency | VERIFIED | Initial push idempotency and concurrent reminder processors result in one provider occurrence. |
| Reminder stops after reveal/revoke/expiry | VERIFIED | Automated stop-condition matrix covers first reveal, revoke, delivery expiry, organization disabled, page disabled and page revoked. |
| Invalid FCM tokens retired | VERIFIED | Invalid-token test disables/deactivates device, clears protected token and avoids raw-token audit leakage. |
| Firebase backend implementation | VERIFIED | Canonical Firebase Admin provider and durable reminder processor are covered by backend suite. |
| Firebase provider credentials in live environment | UNVERIFIED | Repository/CI does not provide admissible real credential evidence. Missing credentials fail closed. |
| Live FCM provider acceptance | UNVERIFIED | No admissible real-device provider-acceptance evidence found. |
| Physical notification receipt | UNVERIFIED | Requires a real registered Android device and observed notification. |
| Push tap opens correct live delivery | UNVERIFIED | Requires physical notification receipt and tap observation on the same real delivery. |
| Live SMS | UNVERIFIED | No real SMS provider delivery evidence found. Automated disabled/test gateway behavior is not live evidence. |
| Canonical API configuration | VERIFIED | Mobile default is exactly `https://testapi.da.gov.kw`; QA exact HTTPS/no-fallback invariant passes. |
| TLS source policy | VERIFIED | QA source gate rejects HTTP fallback and common trust-all certificate hooks in production source; `trustAllBypassDetected=false`. |
| Live TLS handshake to canonical API | BLOCKED | QA execution environments did not provide admissible successful live reachability evidence. No `-k`, trust-all, certificate callback, or HTTP downgrade was used. Owner: `worker/da-secure-iis-deployment-closure` / deployment environment. |
| APK build | VERIFIED | QA run `32914294391` built `app-debug.apk`; raw APK SHA-256 `f6a5397d2135bfa86d2202be64756e6181f0eb243aa9acdec24253dbe18cde42`. |
| APK artifact upload | VERIFIED | `da-secure-final-e2e-debug-apk`, artifact ID `9587733258`, artifact ZIP digest `sha256:482bae327deeb80ef251858365031288194434b18c40d183518d99ec2a48a6ef`. |
| Physical APK/device smoke | UNVERIFIED | No physical Android execution evidence available in repository CI. |
| Full live E2E | BLOCKED | Cannot be VERIFIED until real SMS/OTP provider evidence where required, Firebase Admin credential, real registered Android device/token, provider acceptance, physical receipt, tap-to-correct-delivery, secure reveal, counter/audit observation, and reminder stop are captured end-to-end. |

## Final QA automated evidence

Evidence-bearing QA workflow run `32914294391` on QA checkpoint `29525857f858c43e5bc26814a70a7dd54bb3cf97`:

- Backend Release: PASS, 0 errors
- Backend/Admin/security tests: 127 / 127 PASS
- Admin route smoke: PASS in Arabic RTL and English LTR across all seven requested major Admin route groups
- Canonical HTTPS/static TLS policy: PASS
- Backend evidence artifact: `da-secure-final-e2e-backend-evidence`, artifact ID `9587616887`, ZIP digest `sha256:b0262ce3f2f22b2deb85e8b98709a80cc6581ba52d06c4fb8cb5bf6742220609`
- Flutter format: PASS, 30 files / 0 changed
- Flutter analyze: PASS, `No issues found`
- Flutter tests: 66 / 66 PASS
- Android identity: PASS (`com.qr.mobile.da`, `DA Secure`, matching Firebase client package)
- Debug APK build: PASS
- Raw APK SHA-256: `f6a5397d2135bfa86d2202be64756e6181f0eb243aa9acdec24253dbe18cde42`
- APK artifact upload: PASS, artifact ID `9587733258`

The two additional Flutter QA tests verify the exact canonical HTTPS API and reject HTTP fallback/common trust-all TLS hooks. The two additional backend theory cases execute the requested Admin route set through the authenticated MVC pipeline in both cultures.

## Candidate convergence evidence

Exact production candidate workflow run `32912606115` on `e9c62cd8ba215e5f1e038cdddb9776acc70990bb`:

- Backend Release: PASS
- Backend tests: 125 / 125 PASS
- Flutter format: PASS (0 changed)
- Flutter analyze: PASS (`No issues found`)
- Flutter tests: 64 / 64 PASS
- Android identity: PASS
- Debug APK: PASS
- APK artifact upload: PASS, artifact ID `9587142065`

## External evidence boundary

The static QA probe intentionally reports:

- `liveTlsHandshake`: `UNVERIFIED`
- `loginRoute`: `UNVERIFIED`
- `adminUnauthenticatedRoutes`: `UNVERIFIED`
- `liveSms`: `UNVERIFIED`
- `liveFcm`: `UNVERIFIED`

These values are intentional. They must not be promoted by mocks or source-level assertions. LIVE FCM requires all six user-mandated facts: real Firebase Admin credential, real registered Android device, real stored FCM token, provider acceptance, physical receipt, and tap opening the correct delivery.

## Tracked PR status at QA start

| PR | State | Role in final QA |
|---|---|---|
| #16 | OPEN / mergeable | Flutter real-runtime implementation. Head observed `1404a67d215dcd151cb7a85094bcef5d7981594a`. |
| #17 | OPEN / mergeable | Firebase backend/durable reminder implementation. Head `4ab847b76e93e6b92bdaa9758d441c3ec0534c06`; explicitly declares LIVE FCM UNVERIFIED. |
| #18 | OPEN / mergeable | Release/QA/security harness. Head `438a45165cfe5823601af120c61c79d7be73295a`; older APK observations are superseded by convergence and final-QA exact-head evidence. |
| #19 | OPEN / mergeable | Localization QA evidence. Head `f0c2228cca2bd91bc0c16cdd72ae2d93ceb18494`; reports 4 P1 localization gaps. |

## Tracked worker branches at QA start

`worker/da-secure-admin-hotfix-closure`, `worker/da-secure-admin-runtime-regression`, and `worker/da-secure-iis-deployment-closure` all resolved to candidate SHA `e9c62cd8ba215e5f1e038cdddb9776acc70990bb`. They therefore had no distinct closure commit beyond the candidate at that checkpoint.

## Release decision

**Release status: BLOCKED FOR FINAL LIVE E2E SIGN-OFF.**

The candidate has strong automated backend/mobile/security/Admin-route/APK evidence. Final sign-off still requires the four P1 Admin localization gaps to be closed and re-verified, plus admissible live environment/device evidence for the external legs. No mock, emulator-only provider stub, source assertion, or CI artifact may be promoted to LIVE FCM/SMS evidence.
