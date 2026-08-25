# DA Secure — External Dependency Verification Matrix

Use these evidence levels exactly:

- **SOURCE VERIFIED** — repository/configuration inspected.
- **AUTOMATED VERIFIED** — deterministic automated tests passed on the cited SHA.
- **RUNTIME VERIFIED** — executed in a capable runtime without proving an external provider/device outcome.
- **LIVE EXTERNAL VERIFIED** — actual external service/device outcome observed and tied to exact provenance.
- **UNVERIFIED** — evidence is not present.
- **BLOCKED** — required dependency/input is unavailable.

| Dependency | Source | Automated | Runtime | Live external | Current factual state |
|---|---|---|---|---|---|
| SMS gateway | Existing provider architecture inspected | OTP/security tests included in backend gate | Provider path can fail closed | No real OTP receipt evidence | **LIVE SMS: UNVERIFIED** |
| Firebase Admin credentials | Runtime credential loading path exists; no server credential is committed | Provider fail-closed tests included when integrated | Requires configured runtime credentials | No real provider acceptance + device receipt evidence on release-harness SHA | **LIVE FCM: UNVERIFIED** |
| Real Android device | Flutter Android project exists | Widget/unit tests do not substitute for hardware | APK build/install required | No exact-SHA device install evidence | **UNVERIFIED** |
| Live FCM receipt | Safe payload/provider code exists | Provider/routing tests exist on active worker branches | Requires Firebase credentials and registered token | No exact-SHA received notification evidence | **UNVERIFIED** |
| SQL Server | Provider/migration architecture exists | Migration/script tests are part of backend suites where present | No release-harness live DB application | No live target SQL Server migration evidence | **UNVERIFIED** |
| Canonical host `testapi.da.gov.kw` | Canonical URL fixed in governance/config | Safe smoke harness added | CI smoke performs default TLS validation | Does not by itself prove SMS/FCM/device E2E | **PENDING RELEASE-HARNESS CI** |
| Official Al Diwan crest | Governance explicitly rejects demo SVG as official identity | N/A | Approved binary not found in inspected integration tree | Owner-approved visual asset not proven | **BLOCKED** |

## Live FCM verification minimum

Do not mark LIVE FCM VERIFIED unless one evidence package identifies all of:

1. exact source branch and commit;
2. runtime Firebase Admin credentials configured without repository exposure;
3. a real registered Android device/token;
4. provider accepted result/message id or equivalent provider evidence;
5. notification receipt on that real device;
6. routing-only payload with no protected message content.

## Live SMS verification minimum

Do not mark LIVE SMS VERIFIED unless a real configured SMS provider successfully delivers the OTP to the intended registered test number, with exact source/runtime provenance and without logging the OTP.
