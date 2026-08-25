# DA Secure Localization Gap Register

Audit role: **Localization / Arabic RTL / English LTR QA only**

Production baseline: `feat/secure-qr-mobile-app-isolated` @ `7d0cdfaef1a4ce1292c19212f19fb6d96e880a16`

Strongest mobile candidate reviewed read-only: `lead/da-secure-runtime-convergence`; live head re-fetched as `e9c62cd8ba215e5f1e038cdddb9776acc70990bb` while this register was assembled.

No finding below was fixed on this branch.

## Counts

| Severity | Count |
|---|---:|
| P0 | 0 |
| P1 | 4 |
| P2 | 5 |
| COSMETIC | 1 |

Bug-classified visible hardcoded/localization-bypass occurrences: **18**.

Count method for the 18 occurrences: Organizations Edit (2), Settings General (2), Organizations inline brand (1), Mobile Delivery panel raw values (3), Mobile Delivery History raw status choices/output (6), Mobile Delivery Details raw values (3), Change Password hardcoded success message (1). Dynamic Identity/DataAnnotation errors and bidi gaps are defects but are not added to that hardcoded-occurrence count.

## P0

None found.

## P1

### LQA-001 — Organization Edit branding policy is English-only in Arabic

- **Surface:** Admin → Organizations → Create/Edit
- **Language:** Arabic
- **Severity:** P1
- **Result:** FAIL
- **File:** `src/SecureQrPortal/Areas/Admin/Views/Organizations/Edit.cshtml`
- **Line:** 20-21
- **Defect:** The branding policy block forces `Branding.EnglishName` and the sentence `System branding is fixed to Al Diwan Al Amiri...` regardless of selected culture.
- **Why it matters:** Arabic administrators receive a mixed-language policy/identity block on a core management form.
- **Owner:** Admin UX / Final Convergence
- **Expected closure:** Select the localized brand name and localized policy/help copy from the active culture. Preserve the fixed-brand business rule.

### LQA-002 — General Settings branding policy/help is English-only in Arabic

- **Surface:** Admin → Settings → General
- **Language:** Arabic
- **Severity:** P1
- **Result:** FAIL
- **File:** `src/SecureQrPortal/Areas/Admin/Views/Settings/General.cshtml`
- **Line:** 14, 22
- **Defect:** Branding policy description and `Fixed system identity:` help text are unconditional English.
- **Why it matters:** A mandatory settings surface is not fully localized even though Arabic branding and `UiText` infrastructure exist.
- **Owner:** Admin UX / Final Convergence
- **Expected closure:** Localize both help strings; retain the read-only fixed identity behavior.

### LQA-003 — Mobile Delivery exposes internal raw state/enum values

- **Surface:** Admin → QR Details mobile-delivery panel; Mobile Delivery History; Mobile Delivery Details
- **Language:** Arabic and English
- **Severity:** P1
- **Result:** FAIL Arabic / PARTIAL English
- **Files / lines:**
  - `src/SecureQrPortal/Areas/Admin/Views/Shared/_MobileDeliveryQrPanel.cshtml` — 21, 25, 75
  - `src/SecureQrPortal/Areas/Admin/Views/MobileDelivery/History.cshtml` — 31-35, 73
  - `src/SecureQrPortal/Areas/Admin/Views/MobileDelivery/Details.cshtml` — 23-25, 37
- **Defect:** Raw values including Secure Page status, access-limit mode, delivery status and reminder unit are emitted directly. The History filter exposes `CREATED`, `PROVIDER_ACCEPTED`, `SEND_FAILED`, `REVEALED`, `REVOKED` as visible option labels.
- **Why it matters:** Arabic receives English/machine codes; English receives implementation terminology rather than polished product terminology. It also creates terminology inconsistency with already localized status strings such as Opened/Unread.
- **Owner:** Final Convergence / Admin Mobile Delivery
- **Expected closure:** One centralized presentation map for delivery status, source status, access-limit mode and reminder units in Arabic/English. Keep `Firebase` as a technical constant.

### LQA-004 — Change Password errors/success are not Arabic-localized

- **Surface:** Account → Change Password
- **Language:** Arabic
- **Severity:** P1
- **Result:** FAIL
- **Files / lines:**
  - `src/SecureQrPortal/ViewModels/AccountViewModels.cs` — ChangePasswordVm attributes
  - `src/SecureQrPortal/Controllers/AccountController.cs` — 118-124
- **Defect:** Default DataAnnotations and Identity `e.Description` are passed through without a localization resource; success is hardcoded as `Password changed.`.
- **Why it matters:** Validation and completion states are required localization surfaces and can visibly switch back to English in Arabic mode.
- **Owner:** Final Convergence / Admin Auth
- **Expected closure:** Localize validation/result presentation without weakening Identity validation or authentication behavior.

## P2

### LQA-005 — Admin QR identifiers/timestamps are not consistently bidi-isolated

- **Surface:** QR Registry / QR Details
- **Language:** Arabic RTL
- **Severity:** P2
- **Files / lines:**
  - `src/SecureQrPortal/Areas/Admin/Views/Qr/Index.cshtml` — 64-76, 88-96
  - `src/SecureQrPortal/Areas/Admin/Views/Qr/Details.cshtml` — identity/timeline/history sections
- **Defect:** URLs are explicitly LTR, but QR references, hashes, some timestamps, admin/user values, browser/device strings and timeline values are not uniformly wrapped in an LTR/bidi-isolated presentation container.
- **Risk:** Mixed RTL/LTR identifiers can be visually reordered or punctuation can attach to the wrong segment, which is a correctness concern for references used in support/audit workflows.
- **Owner:** Admin UX
- **Expected closure:** Apply a reusable identifier/value bidi-isolation presentation class/component to technical identifiers without translating them.

### LQA-006 — Flutter Inbox mixes RTL labels and numeric/date values without bidi boundary

- **Surface:** Flutter Organization Inbox
- **Language:** Arabic RTL
- **Severity:** P2
- **File:** `mobile/da_secure/lib/features/inbox/presentation/inbox_screen.dart`
- **Line:** 211-234
- **Defect:** `remainingLabel`, `sentLabel`, and `expiryLabel` are interpolated directly with server-formatted values in one Text run.
- **Risk:** Dates/numbers can reorder punctuation or read ambiguously in RTL.
- **Owner:** Flutter Runtime
- **Expected closure:** Isolate value spans or use locale-/direction-aware rich text while preserving the existing localized labels.

### LQA-007 — Flutter attachment names/sizes are not bidi-isolated

- **Surface:** Flutter Secure Message View
- **Language:** Arabic RTL
- **Severity:** P2
- **File:** `mobile/da_secure/lib/features/inbox/presentation/secure_message_screen.dart`
- **Line:** 114-126
- **Defect:** Dynamic `attachment.name` and `sizeLabel` are rendered directly in the inherited RTL context.
- **Risk:** A filename containing Latin, digits and punctuation can display in a misleading order.
- **Owner:** Flutter Runtime
- **Expected closure:** Isolate dynamic filenames/technical size strings while keeping the Attachments heading localized.

### LQA-008 — Flutter bottom navigation has no localized labels

- **Surface:** Flutter Inbox bottom navigation
- **Language:** Arabic and English
- **Severity:** P2
- **File:** `mobile/da_secure/lib/features/inbox/presentation/inbox_screen.dart`
- **Line:** 70-77
- **Defect:** All three `NavigationDestination` entries use `label: ''`.
- **Risk:** Visible bottom navigation intentionally becomes icon-only, but localized destination naming/accessibility semantics are absent. This weakens bilingual usability and screen-reader clarity.
- **Owner:** Flutter Runtime
- **Expected closure:** Supply localized Home/Inbox/Profile labels or equivalent localized semantics without changing navigation behavior.

### LQA-009 — Flutter date/time presentation is fixed-format, not locale-aware

- **Surface:** Flutter Inbox / delivery metadata
- **Language:** Arabic and English
- **Severity:** P2
- **File:** `mobile/da_secure/lib/runtime/app_runtime.dart`
- **Line:** `_formatDate`
- **Defect:** Runtime formats both locales as `yyyy-MM-dd HH:mm`.
- **Risk:** It is stable and unambiguous, but it does not implement a bilingual date/time presentation contract and leaves Latin-order date values inside Arabic UI.
- **Owner:** Flutter Runtime
- **Expected closure:** Define one intentional date/time contract per locale, then bidi-isolate the produced value. Do not change authoritative timestamps/time-zone semantics.

## COSMETIC

### LQA-010 — Organizations list inline brand forces English name

- **Surface:** Admin → Organizations list
- **Language:** Arabic
- **Severity:** COSMETIC
- **File:** `src/SecureQrPortal/Areas/Admin/Views/Organizations/Index.cshtml`
- **Line:** 20
- **Defect:** The inline brand block shows `Branding.EnglishName` although the main layout correctly selects `Branding.Name(ar)`.
- **Owner:** Admin UX
- **Expected closure:** Reuse the same culture-aware brand display as the layout.

## Checked technical values — not bugs

The following were deliberately not classified as translation defects when used as technical identifiers/controls:

- `QR`
- `Firebase`
- `IP`
- `DA Secure`
- `PNG`
- `SMS`
- `WhatsApp`
- `SQLite`
- `SQL Server`
- `REVOKE`, `RESTORE`, `DELETE`, `RESET`, `REGENERATE`
- audit action/entity codes when presented specifically as audit evidence

Technical values still require bidi isolation where applicable; “not translated” does not mean “safe to render without direction controls.”

## Surfaces with no production localization defect found by static review

- Admin base layout culture/dir selection
- Admin Dashboard labels
- Secure Pages primary labels/status/access-policy terminology
- Access Log primary labels and localized event names
- Database / Backup primary labels
- Admin Login / CAPTCHA visible labels and generic login failure
- Flutter Splash
- Flutter Mobile Number
- Flutter OTP
- Flutter optional Biometrics
- Flutter secure-delivery terminal strings (expired/revoked/reveal-limit/invalid credentials/service unavailable)
- exact required Arabic/English notification heading source values
- zero-attachment presentation contract

These statements are **static/code-path findings** unless backed by the Flutter automated evidence. They do not convert Admin browser widths into PASS.

## Required closure order

1. **P1:** Localize Mobile Delivery state presentation centrally.
2. **P1:** Remove English-only branding help from Organization Edit and General Settings Arabic paths.
3. **P1:** Localize Change Password validation/Identity result/success presentation.
4. **P2:** Add bidi isolation contracts for Admin QR values and Flutter dynamic metadata/attachments.
5. **P2:** Restore localized/accessible bottom navigation labels.
6. **P2:** Define intentional localized date/time presentation.
7. **COSMETIC:** Culture-aware inline Organizations brand.

No production fix should be taken from this QA branch. Owners should implement on their active production branches and return exact-head evidence to Release QA.

**NO MAIN MERGE.**
