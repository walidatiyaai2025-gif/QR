import 'package:flutter/material.dart';

class DaStrings {
  const DaStrings._(this.isArabic);

  final bool isArabic;

  static DaStrings of(BuildContext context) {
    final languageCode = Localizations.localeOf(context).languageCode;
    return DaStrings._(languageCode != 'en');
  }

  String get diwanArabic => 'الديوان الأميري';
  String get diwanEnglish => 'AL DIWAN AL AMIRI';
  String get appName => 'DA Secure';

  String get signIn => isArabic ? 'تسجيل الدخول' : 'Sign in';
  String get signOut => isArabic ? 'تسجيل الخروج' : 'Sign out';
  String get mobilePrompt => isArabic
      ? 'أدخل رقم الجوال المسجل لتسجيل الدخول'
      : 'Enter the registered mobile number to sign in.';
  String get mobileNumber => isArabic ? 'رقم الجوال' : 'Mobile number';
  String get requestOtp =>
      isArabic ? 'طلب رمز التحقق' : 'Request verification code';
  String get verifyCode => isArabic ? 'تحقق من الرمز' : 'Verify code';
  String get otpPrompt => isArabic
      ? 'أدخل رمز التحقق المكوّن من 6 أرقام'
      : 'Enter the 6-digit verification code.';
  String get verify => isArabic ? 'تحقق' : 'Verify';
  String get resend => isArabic ? 'إعادة إرسال الرمز' : 'Resend code';
  String resendIn(int seconds) =>
      isArabic ? 'إعادة الإرسال خلال $seconds ث' : 'Resend in ${seconds}s';

  String get biometricTitle =>
      isArabic ? 'تفعيل بصمة الجهاز؟' : 'Enable device biometrics?';
  String get biometricBody => isArabic
      ? 'البصمة اختيارية ولا تستبدل اسم المستخدم وكلمة المرور الخاصة بالرسالة الآمنة.'
      : 'Biometrics are optional and never replace the secure-message username and password.';
  String get enableBiometric =>
      isArabic ? 'تفعيل البصمة' : 'Enable biometrics';
  String get notNow => isArabic ? 'ليس الآن' : 'Not now';

  String get inbox => isArabic ? 'الوارد' : 'Inbox';
  String get secureLinks => isArabic ? 'الروابط الآمنة' : 'Secure links';
  String get inboxEmpty => isArabic
      ? 'لا توجد رسائل آمنة حاليًا.'
      : 'There are no secure messages right now.';
  String get retry => isArabic ? 'إعادة المحاولة' : 'Retry';
  String get loading => isArabic ? 'جارٍ التحميل...' : 'Loading...';
  String get fixedMessageHeading => isArabic
      ? 'لديك رسالة جديدة اضغط هنا لاستعراض الرسالة'
      : 'You have a new message. Tap here to view it.';
  String get remainingReveals =>
      isArabic ? 'المشاهدات المتبقية' : 'Remaining reveals';
  String get sentAt => isArabic ? 'وقت الإرسال' : 'Sent';
  String get expiresAt => isArabic ? 'تنتهي' : 'Expires';

  String get secureMessageLogin =>
      isArabic ? 'تسجيل دخول الرسالة الآمنة' : 'Secure message sign in';
  String get username => isArabic ? 'اسم المستخدم' : 'Username';
  String get password => isArabic ? 'كلمة المرور' : 'Password';
  String get revealMessage =>
      isArabic ? 'استعراض الرسالة' : 'View secure message';
  String get secureMessage =>
      isArabic ? 'الرسالة الآمنة' : 'Secure message';
  String get attachments => isArabic ? 'المرفقات' : 'Attachments';

  String get expired =>
      isArabic ? 'انتهت صلاحية الرسالة.' : 'This message has expired.';
  String get revoked =>
      isArabic ? 'تم إلغاء الرسالة.' : 'This message was revoked.';
  String get limitReached => isArabic
      ? 'تم الوصول إلى الحد المسموح للمشاهدة.'
      : 'The reveal limit has been reached.';
  String get authenticationFailed => isArabic
      ? 'بيانات اعتماد الرسالة غير صحيحة.'
      : 'The secure-message credentials are invalid.';
  String get serviceUnavailable => isArabic
      ? 'الخدمة غير متصلة حاليًا. حاول مرة أخرى لاحقًا.'
      : 'The service is not connected right now. Try again later.';
}
