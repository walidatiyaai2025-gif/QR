# DA Secure — Approved Mobile Visual Contract

The owner-supplied screenshots/mockups and official Al Diwan Al Amiri crest are **authoritative**, not inspiration.

Do not replace them with generic Material templates or arbitrary redesigns.

## Fixed identity

- Brand: الديوان الأميري / AL DIWAN AL AMIRI
- App: DA Secure
- Deep navy base
- Gold primary accents/actions
- White primary text
- Muted blue/gray secondary text
- Official Al Diwan Al Amiri crest centered prominently on splash/auth and present in government header treatment
- Arabic RTL first
- English LTR equivalent

## Approved screen family

1. Splash / start screen
2. Mobile Number Login
3. OTP Verification
4. Biometric Enrollment Prompt
5. Organization Inbox / secure links
6. Secure Message Login
7. Secure Message View

## Visual observations from approved references

### Splash

- Full-height deep navy background with subtle tonal government/Kuwait architectural motif.
- Official crest near upper-middle, with Al Diwan Al Amiri wordmark treatment.
- Large Arabic `الديوان الأميري` hierarchy beneath branding.
- No generic lock/QR hero icon.

### Mobile Number Login

- Back arrow, centered crest, bold `تسجيل الدخول` heading.
- Supporting text: enter mobile number registered for login.
- Phone input with Kuwait country code treatment.
- Gold filled primary button `طلب رمز التحقق`.
- Thin divider with `أو`.
- Secondary outlined gold action for username-based alternative only if product/backend explicitly retains it; do not invent behavior.
- Terms/privacy microcopy at bottom.

### OTP + Biometrics

- RTL OTP boxes with six visual digits.
- resend timer below.
- premium bordered biometrics panel with face/fingerprint iconography.
- gold `تفعيل البصمة` primary action and `ليس الآن` secondary action.
- biometric enrollment is optional.

### Organization Inbox

- Organization name centered/high prominence, e.g. `وزارة الخارجية`.
- Gold subtitle `الروابط الآمنة` / inbox context.
- top notification bell with numeric badge.
- section header `الوارد` with count of open secure links.
- filter control.
- stacked large navy cards with subtle blue border/highlight and gold icon container/action.
- cards show fixed product heading, short safe metadata, reveal/open remaining indicator, send time, expiry where relevant.
- bottom navigation includes main/home, inbox, profile using the approved navy/gold treatment.

### Secure Login

- secure-link card summary followed by username/password fields.
- password visibility affordance.
- forgot-password link only if it exists in real backend/product rules; do not invent.
- gold filled `تسجيل الدخول` action.

### Secure Message View

- top app bar with back affordance and secure/shield indicator.
- gold metadata strip for message number/reveal count/remaining expiry time as supported by real data.
- body shows organization, fixed message heading, exact sanitized Text Editor content and date/time.
- attachment section appears only when attachments exist.
- zero-attachment message must render cleanly without empty attachment chrome.

## Fixed message copy

Arabic: `لديك رسالة جديدة اضغط هنا لاستعراض الرسالة`

English: `You have a new message. Tap here to view it.`

No admin-defined title/subject field.

## Responsive visual QA

Capture exact-SHA screenshots at minimum widths: 360, 375, 390, 412, 430.

Validate:

- no clipping/overflow
- correct RTL/LTR order
- approved card geometry/density
- typography hierarchy
- gold button hierarchy
- crest proportions
- bottom navigation
- loading/empty/success/error/retry states

## Source-asset warning

The repository already contains `src/SecureQrPortal/wwwroot/images/sample/diwan-logo.svg`, but that file explicitly labels itself a **demo asset**. It is not approved as the mobile crest. Do not use it to claim branding parity. Import the owner-supplied official crest/reference assets when binary asset handling is available, without materially redrawing/distorting them.
