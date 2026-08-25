# DA Secure Localization / RTL / LTR QA

## Scope and provenance

Role: **DA Secure Localization / RTL QA**

Worker branch: `worker/da-secure-localization-rtl-qa`

Worker starting baseline: `feat/secure-qr-mobile-app-isolated` @ `7d0cdfaef1a4ce1292c19212f19fb6d96e880a16`.

Live production candidates read-only during this audit:

- Integration: `feat/secure-qr-mobile-app-isolated` @ `7d0cdfaef1a4ce1292c19212f19fb6d96e880a16`.
- Runtime convergence: `lead/da-secure-runtime-convergence`; live head moved during the audit and was re-fetched at `e9c62cd8ba215e5f1e038cdddb9776acc70990bb`.
- Flutter runtime PR #16: live head moved from `e6e91a69d85734be366488a5bb0a59f1d1312585` to `1404a67d215dcd151cb7a85094bcef5d7981594a`. The only delta between those two heads is Android launcher assets; audited localization/presentation files are unchanged.
- Firebase/reminders PR #17 and release-harness PR #18 were inspected for ownership only. No production code from those branches was modified here.

This worker changed only QA tests and QA documentation. No Razor view, CSS, controller, service, or `mobile/da_secure/lib/**` production file is modified.

## Method and evidence rules

The audit combined:

1. Static review of Admin Razor/layout/localization/CSS from the exact integration baseline.
2. Read-only review of the strongest current Flutter localization/presentation candidate.
3. Existing exact-head Flutter QA evidence from PR #16 run `32910577114`: format PASS, analyze PASS, **60 Flutter tests PASS**. That run includes Arabic and English responsive widget tests, RTL/LTR directionality, empty states, zero attachments, push-routing UI contracts, and secure-message semantics. The job failed later at APK resource linking, not at the Flutter tests.
4. New QA-only tests added on this worker branch. They are **UNVERIFIED** because the repository has no workflow trigger for this worker branch / PR base and the local execution environment could not obtain the toolchain from the network. They are not counted as PASS.
5. Admin browser rendering was not available. Static CSS/layout checks are reported separately from visual rendering. No Admin width is falsely marked PASS.

Technical constants intentionally excluded from localization defects include `QR`, `Firebase`, `IP`, `DA Secure`, `PNG`, `SMS`, `WhatsApp`, `SQLite`, `SQL Server`, and confirmation tokens such as `REVOKE`, `RESTORE`, `DELETE`, and `RESET`.

## Executive result

- **Admin Arabic:** FAIL — multiple user-visible English/raw status localization bypasses remain.
- **Admin English:** PARTIAL — primary labels are English, but Mobile Delivery exposes internal machine statuses such as `PROVIDER_ACCEPTED` instead of user-facing terminology.
- **Mobile Arabic:** PARTIAL — critical strings and widget directionality pass existing automated tests; bidi isolation/accessibility gaps remain.
- **Mobile English:** PARTIAL — critical strings and widget directionality pass existing automated tests; bottom-navigation localization/accessibility gap remains.
- **RTL:** FAIL overall because bidi-sensitive dynamic values are not consistently isolated, even though root/widget directionality itself is correct.
- **LTR:** PASS for directional behavior; localization completeness is still PARTIAL.
- **P0:** 0
- **P1:** 4
- **P2:** 5
- **COSMETIC:** 1
- **Bug-classified visible hardcoded/localization-bypass occurrences:** 18. This count excludes intentional technical constants and audit machine values classified as technical evidence.

## Surface audit

| Surface | Language | Width | Result | Defect / evidence | File | Line | Severity | Owner |
|---|---|---:|---|---|---|---:|---|---|
| Admin layout/navigation | AR | Desktop/static | PASS (static) | `<html lang="ar" dir="rtl">`, Arabic brand name/navigation, logical CSS and RTL sidebar behavior are present. Browser rendering unavailable. | `src/SecureQrPortal/Areas/Admin/Views/Shared/_AdminLayout.cshtml` | 14-20 | — | — |
| Admin layout/navigation | EN | Desktop/static | PASS (static) | `<html lang="en" dir="ltr">` and English navigation/brand path are present. | same | 14-20 | — | — |
| Dashboard | AR | Browser widths | UNVERIFIED | Dashboard labels are localized statically; no browser-render evidence was available for all required widths. | `Areas/Admin/Views/Dashboard/Index.cshtml` | whole view | — | Release QA |
| Dashboard | EN | Browser widths | UNVERIFIED | Static localization reviewed; no browser-render evidence. | same | whole view | — | Release QA |
| Mobile Delivery panel | AR | Static | FAIL | Source Secure Page status, access-limit mode and latest delivery status render raw enum/state values. | `Areas/Admin/Views/Shared/_MobileDeliveryQrPanel.cshtml` | 21, 25, 75 | P1 | Final Convergence/Admin |
| Mobile Delivery history | AR | Static | FAIL | Status filter shows raw `CREATED`, `PROVIDER_ACCEPTED`, `SEND_FAILED`, `REVEALED`, `REVOKED`; rows show raw `DeliveryStatus`. | `Areas/Admin/Views/MobileDelivery/History.cshtml` | 31-35, 73 | P1 | Final Convergence/Admin |
| Mobile Delivery history | EN | Static | PARTIAL | Same raw machine codes are understandable only as implementation states, not polished user-facing English terminology. | same | 31-35, 73 | P1 | Final Convergence/Admin |
| Mobile Delivery details | AR | Static | FAIL | `DeliveryStatus`, source status and reminder unit are rendered raw. | `Areas/Admin/Views/MobileDelivery/Details.cshtml` | 23-25, 37 | P1 | Final Convergence/Admin |
| Organizations list | AR | Static | PARTIAL | Main labels are localized, but inline brand block forces `Branding.EnglishName`. | `Areas/Admin/Views/Organizations/Index.cshtml` | 20 | COSMETIC | Admin UX |
| Organizations edit | AR | Static | FAIL | Fixed branding policy note is English-only and forces the English brand name. | `Areas/Admin/Views/Organizations/Edit.cshtml` | 20-21 | P1 | Admin UX |
| Organizations | EN | Static | PASS | Primary labels/forms are English and organization names switch by selected locale. | `Areas/Admin/Views/Organizations/*` | reviewed | — | — |
| QR Registry | AR/EN | Static | PARTIAL | Status/access-policy labels are localized and URLs are LTR; QR references and multiple date/user values are not consistently bidi-isolated in list/card output. | `Areas/Admin/Views/Qr/Index.cshtml` | 64-76, 88-96 | P2 | Admin UX |
| QR Details | AR/EN | Static | PARTIAL | Most labels are explicitly bilingual; QR reference/masked token, timeline IP/browser/device and audit/revocation values are not consistently isolated as bidi-sensitive values. | `Areas/Admin/Views/Qr/Details.cshtml` | 48-69, 139-151, 300+ | P2 | Admin UX |
| Secure Pages list/edit | AR | Static | PASS (static) | Titles/content fields explicitly use RTL/LTR as appropriate; QR reference uses `dir="ltr"`; status/access policy pass through `UiText`. Visual browser widths remain unverified. | `Areas/Admin/Views/SecurePages/Index.cshtml`, `Edit.cshtml` | reviewed | — | Release QA |
| Secure Pages list/edit | EN | Static | PASS (static) | English title/content use LTR and localized labels. | same | reviewed | — | — |
| Access Logs | AR/EN | Static | PASS (static) | Event labels use `UiText`; QR references/IP are rendered as LTR/numeric values. Browser/device/country are dynamic technical values. | `Areas/Admin/Views/Logs/Access.cshtml` | reviewed | — | — |
| Audit Log | AR/EN | Static | PASS with technical-values exception | Column labels/localized empty state are localized. Action/entity/details are retained as internal audit evidence and classified TECHNICAL VALUE, not translation bugs. | `Areas/Admin/Views/Logs/Audit.cshtml` | reviewed | — | — |
| Settings / General | AR | Static | FAIL | Branding policy explanation and fixed-system-identity help are English-only. | `Areas/Admin/Views/Settings/General.cshtml` | 14, 22 | P1 | Admin UX |
| Settings / Database / Backup | AR/EN | Static | PASS (static) | Labels/messages use `UiText`; provider names are technical constants. Dynamic provider diagnostics may remain technical English and are not classified as UI labels. | `Areas/Admin/Views/Settings/Database.cshtml`, `Backup.cshtml` | reviewed | — | — |
| Admin Login / CAPTCHA | AR | Static | PASS | Visible labels/help/refresh text are bilingual; email and CAPTCHA answer are LTR; server login failure is explicitly Arabic/English by current UI culture. | `Views/Account/Login.cshtml`, `Controllers/AccountController.cs` | login failure around 139+ | — | — |
| Admin Login / CAPTCHA | EN | Static | PASS | English path is explicitly present. | same | reviewed | — | — |
| Change Password | AR | Static | FAIL | Default DataAnnotations/Identity error descriptions are not localized, and success message is hardcoded English `Password changed.`. | `ViewModels/AccountViewModels.cs`; `Controllers/AccountController.cs` | VM 16; controller 118-124 | P1 | Final Convergence/Admin |
| Flutter Splash | AR/EN | 360-430 | PASS in existing widget suite | Localized branding/status strings; no overflow in tested widths. | `mobile/da_secure/lib/features/splash/presentation/splash_screen.dart` | reviewed | — | Flutter runtime |
| Flutter Mobile Number | AR/EN | 360-430 | PASS in existing widget suite | Localized copy; mobile input forced LTR for identifier correctness. | `.../mobile_number_screen.dart` | reviewed | — | Flutter runtime |
| Flutter OTP | AR/EN | 360-430 | PASS in existing widget suite | Localized verification/resend states; OTP field is LTR. | `.../otp_screen.dart` | reviewed | — | Flutter runtime |
| Flutter Biometrics | AR/EN | 360-430 | PASS in existing widget suite | Bilingual optional-biometric wording. | `.../biometric_screen.dart` | reviewed | — | Flutter runtime |
| Flutter Inbox | AR/EN | 360-430 | PARTIAL | Fixed heading/empty/error states localized and responsive tests pass; bottom navigation has empty labels; dynamic date/reveal values are interpolated into RTL text without explicit bidi isolation. | `.../inbox_screen.dart` | 70-77, 211-234 | P2 | Flutter runtime |
| Flutter Secure Message Login | AR/EN | 360-430 | PASS for localization contracts | Expired/revoked/limit/invalid-credentials/service-unavailable strings are bilingual. | `.../secure_login_screen.dart` | reviewed | — | Flutter runtime |
| Flutter Secure Message View | AR/EN | 360-430 | PARTIAL | Fixed heading and zero-attachment state are localized; attachment filenames/size labels are dynamic and not bidi-isolated. | `.../secure_message_screen.dart` | 114-126 | P2 | Flutter runtime |
| Flutter date/time presentation | AR/EN | all | PARTIAL | Runtime uses fixed `yyyy-MM-dd HH:mm` formatting for both languages instead of a locale-aware presentation contract. | `mobile/da_secure/lib/runtime/app_runtime.dart` | `_formatDate` | P2 | Flutter runtime |
| Push routing visible UI | AR/EN | widget | PASS in existing suite | Authenticated/unauthenticated routes preserve destination without exposing protected message body. | existing runtime/widget tests | — | — | Flutter runtime |

## Required notification heading

Required Arabic:

`لديك رسالة جديدة اضغط هنا لاستعراض الرسالة`

Required English:

`You have a new message. Tap here to view it.`

Both exact values are present in `DaStrings.fixedMessageHeading`. Existing UI tests cover the Arabic heading; this worker adds behavior-level regression coverage for both Arabic and English headings. The newly added worker test has not been executed in a CI environment, so it is not promoted to PASS evidence yet.

## Mobile terminal-state localization

Read-only inspection confirms localized visible strings for:

- loading
- retry
- empty inbox
- expired delivery
- revoked delivery
- reveal-limit reached
- invalid secure credentials
- service unavailable
- attachments heading
- zero attachments (attachment chrome omitted)

Runtime failures map through bilingual `AppFailure.messageFor(isArabic)`, avoiding raw backend error text for the principal mobile error states.

## Bidi security / correctness

### Positive controls

- Admin root `dir` follows culture.
- Admin `.numeric` applies `direction:ltr` and `unicode-bidi:isolate`.
- Admin phone/IP/URL fields frequently use `dir="ltr"` or `.numeric`.
- Secure Page edit isolates QR reference with `dir="ltr"` and forces Arabic/English content fields to RTL/LTR respectively.
- Flutter mobile number and OTP fields explicitly use LTR.

### Remaining gaps

1. QR Registry and QR Details do not consistently isolate QR references, hashes, timestamps, usernames/admin names, browser/device strings, and timeline values when embedded in RTL content.
2. Flutter Inbox concatenates localized RTL labels with date/reveal values without an explicit bidi boundary.
3. Flutter attachment filenames and size labels are user-/server-controlled values rendered directly inside RTL context.

These are correctness/security-of-presentation gaps: identifiers must not be visually reordered in a way that can mislead an administrator or recipient.

## Terminology consistency

Preferred/observed contract:

| Concept | Arabic | English | Result |
|---|---|---|---|
| Dashboard | لوحة التحكم | Dashboard | Consistent |
| Organization | الجهة | Organization | Consistent |
| QR | QR | QR | Intentional technical constant |
| Secure Page | الصفحة الآمنة | Secure Page | Consistent |
| Mobile Delivery | إرسال / إرسالات DA Secure | DA Secure Delivery / Deliveries | Mostly consistent |
| Delivery History | سجل الإرسالات | Delivery History | Consistent |
| Opened | تم فتح الرسالة | Message Opened | Consistent |
| Unread | غير مفتوحة | Unread | Consistent |
| Reveal / View | استعراض / مشاهدة | Reveal / View | Context-dependent but understandable |
| Revoked | ملغي / تم إلغاء الرسالة | Revoked | Consistent by noun/sentence context |
| Expired | منتهي / انتهت صلاحية الرسالة | Expired | Consistent by noun/sentence context |
| Reminder | تذكير | Reminder | Consistent except raw unit values in Details |
| Next Reminder | التذكير التالي | Next Reminder | Consistent |
| Firebase | Firebase | Firebase | Intentional constant |
| Attachments | المرفقات | Attachments | Consistent |
| Username | اسم المستخدم | Username | Consistent |
| Password | كلمة المرور | Password | Consistent |
| Mobile Number | رقم الجوال | Mobile number | Consistent |
| Verification Code | رمز التحقق | Verification code | Consistent |

The primary terminology defect is not the translation dictionary; it is UI code bypassing localization and emitting internal status/unit values directly.

## Responsive evidence

### Flutter

Existing PR #16 exact-head test evidence at `e6e91a69d85734be366488a5bb0a59f1d1312585` passed all 60 Flutter tests, including:

- Arabic no-overflow: 360, 375, 390, 412, 430.
- English no-overflow: 360, 375, 390, 412, 430.
- Arabic RTL / English LTR.
- empty inbox.
- secure message zero attachments.

Current PR #16 head `1404a67d215dcd151cb7a85094bcef5d7981594a` differs from that tested head only by launcher image assets, so no audited localization/presentation source changed in that one-commit delta. A separate current-head run was in progress during report assembly and is not treated as completed evidence until it finishes.

### Admin

Static CSS includes responsive table-to-card behavior and a 430px single-column breakpoint. However, no browser/device rendering was available for this worker. Therefore the required Admin widths are **UNVERIFIED**, and the overall width verdict remains UNVERIFIED despite Flutter widget PASS evidence.

## Automated QA added on this branch

`mobile/da_secure/test/localization_qa_test.dart`

- exact required Arabic notification heading
- exact required English notification heading
- bilingual expired/revoked/limit/auth-failure/service-unavailable output
- English zero-attachment output
- critical Arabic/English auth terminology

`tests/SecureQrPortal.Tests/LocalizationContractTests.cs`

- Arabic culture critical Admin terminology
- English culture critical Admin terminology
- Arabic/English status terminology for Active/Expired/Revoked/Limit reached

These tests are behavior/output contracts. They do not modify or patch production implementation.

## Validation limitation

The new worker branch is not targeted by the repository's current push/PR workflows, and the local execution environment could not resolve GitHub/toolchain downloads. Consequently:

- New QA tests added here: **UNVERIFIED**.
- No failure is fabricated.
- No success is claimed without execution.
- Existing exact-head PR #16 test evidence is cited only for the unchanged mobile presentation behavior it actually exercised.

## Handoff

Production fixes belong to **FINAL CONVERGENCE / RELEASE QA** and the active Admin/Flutter owners. This branch is a QA evidence branch only.

**NO MAIN MERGE. NO AUTO-MERGE. DO NOT SELF-MERGE.**
