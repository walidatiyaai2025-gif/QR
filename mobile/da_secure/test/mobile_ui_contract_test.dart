import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/features/auth/presentation/biometric_screen.dart';
import 'package:da_secure/features/auth/presentation/mobile_number_screen.dart';
import 'package:da_secure/features/auth/presentation/otp_screen.dart';
import 'package:da_secure/features/inbox/presentation/inbox_screen.dart';
import 'package:da_secure/features/inbox/presentation/secure_login_screen.dart';
import 'package:da_secure/features/inbox/presentation/secure_message_screen.dart';
import 'package:da_secure/features/splash/presentation/splash_screen.dart';
import 'package:da_secure/presentation/mobile_ui_contracts.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const widths = <double>[360, 375, 390, 412, 430];

  for (final width in widths) {
    testWidgets('Arabic screens render without overflow at ${width}px', (
      tester,
    ) async {
      final screens = <Widget>[
        const SplashScreen(),
        const MobileNumberScreen(),
        const OtpScreen(),
        const BiometricScreen(),
        const InboxScreen(),
        const SecureLoginScreen(deliveryId: 'delivery-test'),
        const SecureMessageScreen(
          deliveryId: 'delivery-test',
          state: SecureMessageUiState(
            phase: SecureDeliveryUiPhase.success,
            organizationName: 'جهة اختبار',
            bodyText: 'نص اختبار',
            attachments: <AttachmentUiModel>[],
          ),
        ),
      ];

      for (final screen in screens) {
        await tester.pumpWidget(
          _TestHost(
            width: width,
            locale: const Locale('ar'),
            child: screen,
          ),
        );
        await tester.pump(const Duration(milliseconds: 20));
        expect(tester.takeException(), isNull);
      }
    });
  }

  testWidgets('English LTR renders at 390px', (tester) async {
    await tester.pumpWidget(
      const _TestHost(
        width: 390,
        locale: Locale('en'),
        child: InboxScreen(),
      ),
    );

    expect(find.text('Inbox'), findsOneWidget);
    expect(
      Directionality.of(tester.element(find.text('Inbox'))),
      TextDirection.ltr,
    );
  });

  testWidgets('Inbox empty state contains no fake delivery cards', (
    tester,
  ) async {
    await tester.pumpWidget(
      const _TestHost(
        width: 390,
        locale: Locale('ar'),
        child: InboxScreen(),
      ),
    );

    expect(find.text('لا توجد رسائل آمنة حاليًا.'), findsOneWidget);
    expect(
      find.text('لديك رسالة جديدة اضغط هنا لاستعراض الرسالة'),
      findsNothing,
    );
  });

  testWidgets(
    'Secure message keeps fixed heading and hides attachments chrome',
    (tester) async {
      await tester.pumpWidget(
        const _TestHost(
          width: 390,
          locale: Locale('ar'),
          child: SecureMessageScreen(
            deliveryId: 'delivery-test',
            state: SecureMessageUiState(
              phase: SecureDeliveryUiPhase.success,
              organizationName: 'جهة اختبار',
              bodyText: 'محتوى آمن',
              attachments: <AttachmentUiModel>[],
            ),
          ),
        ),
      );

      expect(
        find.text('لديك رسالة جديدة اضغط هنا لاستعراض الرسالة'),
        findsOneWidget,
      );
      expect(find.text('المرفقات'), findsNothing);
    },
  );
}

class _TestHost extends StatelessWidget {
  const _TestHost({
    required this.width,
    required this.locale,
    required this.child,
  });

  final double width;
  final Locale locale;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      theme: DaSecureTheme.light,
      locale: locale,
      supportedLocales: const [
        Locale('ar'),
        Locale('en'),
      ],
      localizationsDelegates: GlobalMaterialLocalizations.delegates,
      home: Align(
        alignment: Alignment.topCenter,
        child: SizedBox(
          width: width,
          height: 560,
          child: child,
        ),
      ),
    );
  }
}
