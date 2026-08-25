import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/features/auth/presentation/mobile_number_screen.dart';
import 'package:da_secure/features/auth/presentation/otp_screen.dart';
import 'package:da_secure/features/inbox/presentation/inbox_screen.dart';
import 'package:da_secure/features/inbox/presentation/secure_login_screen.dart';
import 'package:da_secure/features/inbox/presentation/secure_message_screen.dart';
import 'package:da_secure/presentation/mobile_ui_contracts.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('required notification heading is exact in Arabic and English', (
    tester,
  ) async {
    const item = InboxDeliveryUiModel(deliveryId: '42');

    await tester.pumpWidget(
      const _TestHost(
        locale: Locale('ar'),
        child: InboxScreen(
          state: InboxUiState(phase: UiPhase.success, items: [item]),
        ),
      ),
    );
    expect(
      find.text('لديك رسالة جديدة اضغط هنا لاستعراض الرسالة'),
      findsOneWidget,
    );

    await tester.pumpWidget(
      const _TestHost(
        locale: Locale('en'),
        child: InboxScreen(
          state: InboxUiState(phase: UiPhase.success, items: [item]),
        ),
      ),
    );
    expect(
      find.text('You have a new message. Tap here to view it.'),
      findsOneWidget,
    );
  });

  testWidgets('secure delivery terminal states are bilingual', (tester) async {
    const expectations = <SecureDeliveryUiPhase, List<String>>{
      SecureDeliveryUiPhase.expired: [
        'انتهت صلاحية الرسالة.',
        'This message has expired.',
      ],
      SecureDeliveryUiPhase.revoked: [
        'تم إلغاء الرسالة.',
        'This message was revoked.',
      ],
      SecureDeliveryUiPhase.limitReached: [
        'تم الوصول إلى الحد المسموح للمشاهدة.',
        'The reveal limit has been reached.',
      ],
      SecureDeliveryUiPhase.authenticationFailure: [
        'بيانات اعتماد الرسالة غير صحيحة.',
        'The secure-message credentials are invalid.',
      ],
      SecureDeliveryUiPhase.error: [
        'الخدمة غير متصلة حاليًا. حاول مرة أخرى لاحقًا.',
        'The service is not connected right now. Try again later.',
      ],
    };

    for (final entry in expectations.entries) {
      await tester.pumpWidget(
        _TestHost(
          locale: const Locale('ar'),
          child: SecureLoginScreen(
            deliveryId: '42',
            state: SecureLoginUiState(phase: entry.key),
          ),
        ),
      );
      expect(find.text(entry.value[0]), findsOneWidget);

      await tester.pumpWidget(
        _TestHost(
          locale: const Locale('en'),
          child: SecureLoginScreen(
            deliveryId: '42',
            state: SecureLoginUiState(phase: entry.key),
          ),
        ),
      );
      expect(find.text(entry.value[1]), findsOneWidget);
    }
  });

  testWidgets('zero attachment state hides attachment chrome in English', (
    tester,
  ) async {
    await tester.pumpWidget(
      const _TestHost(
        locale: Locale('en'),
        child: SecureMessageScreen(
          deliveryId: '42',
          state: SecureMessageUiState(
            phase: SecureDeliveryUiPhase.success,
            bodyText: 'Safe fixture',
            attachments: <AttachmentUiModel>[],
          ),
        ),
      ),
    );

    expect(
      find.text('You have a new message. Tap here to view it.'),
      findsOneWidget,
    );
    expect(find.text('Attachments'), findsNothing);
  });

  testWidgets('critical auth terminology follows selected locale', (
    tester,
  ) async {
    await tester.pumpWidget(
      const _TestHost(
        locale: Locale('ar'),
        child: MobileNumberScreen(),
      ),
    );
    expect(find.text('تسجيل الدخول'), findsOneWidget);
    expect(find.text('رقم الجوال'), findsOneWidget);
    expect(find.text('طلب رمز التحقق'), findsOneWidget);

    await tester.pumpWidget(
      const _TestHost(locale: Locale('en'), child: OtpScreen()),
    );
    expect(find.text('Verify code'), findsOneWidget);
    expect(find.text('Verify'), findsOneWidget);
    expect(find.text('Resend code'), findsOneWidget);

    await tester.pumpWidget(
      const _TestHost(
        locale: Locale('ar'),
        child: SecureLoginScreen(deliveryId: '42'),
      ),
    );
    expect(find.text('اسم المستخدم'), findsOneWidget);
    expect(find.text('كلمة المرور'), findsOneWidget);

    await tester.pumpWidget(
      const _TestHost(
        locale: Locale('en'),
        child: SecureLoginScreen(deliveryId: '42'),
      ),
    );
    expect(find.text('Username'), findsOneWidget);
    expect(find.text('Password'), findsOneWidget);
  });
}

class _TestHost extends StatelessWidget {
  const _TestHost({required this.locale, required this.child});

  final Locale locale;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      theme: DaSecureTheme.light,
      locale: locale,
      supportedLocales: const [Locale('ar'), Locale('en')],
      localizationsDelegates: GlobalMaterialLocalizations.delegates,
      home: SizedBox(width: 390, height: 640, child: child),
    );
  }
}
