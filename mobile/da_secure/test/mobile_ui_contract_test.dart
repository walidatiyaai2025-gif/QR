import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/features/auth/presentation/biometric_screen.dart';
import 'package:da_secure/features/auth/presentation/mobile_number_screen.dart';
import 'package:da_secure/features/auth/presentation/otp_screen.dart';
import 'package:da_secure/features/inbox/presentation/inbox_screen.dart';
import 'package:da_secure/features/inbox/presentation/secure_login_screen.dart';
import 'package:da_secure/features/inbox/presentation/secure_message_screen.dart';
import 'package:da_secure/features/splash/presentation/splash_screen.dart';
import 'package:da_secure/presentation/mobile_ui_contracts.dart';
import 'package:da_secure/routing/app_navigation_state.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const widths = <double>[360, 375, 390, 412, 430];

  for (final locale in const <Locale>[Locale('ar'), Locale('en')]) {
    for (final width in widths) {
      testWidgets(
        '${locale.languageCode} screens render without overflow at ${width}px',
        (tester) async {
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
                organizationName: 'Test Organization',
                bodyText: 'Safe test fixture',
                attachments: <AttachmentUiModel>[],
              ),
            ),
          ];

          for (final screen in screens) {
            await tester.pumpWidget(
              _TestHost(width: width, locale: locale, child: screen),
            );
            await tester.pump(const Duration(milliseconds: 50));
            expect(tester.takeException(), isNull);
          }
        },
      );
    }
  }

  for (final locale in const <Locale>[Locale('ar'), Locale('en')]) {
    testWidgets(
      '${locale.languageCode} auth screens stay usable on small height with keyboard',
      (tester) async {
        final screens = <Widget>[
          const MobileNumberScreen(),
          const OtpScreen(),
          const SecureLoginScreen(deliveryId: 'delivery-test'),
        ];

        for (final screen in screens) {
          await tester.pumpWidget(
            _TestHost(
              width: 360,
              height: 520,
              locale: locale,
              textScale: 1.2,
              viewInsetsBottom: 220,
              child: screen,
            ),
          );
          await tester.pump(const Duration(milliseconds: 50));
          expect(tester.takeException(), isNull);

          final scrollable = find.byType(Scrollable).first;
          expect(scrollable, findsOneWidget);
        }
      },
    );

    testWidgets(
      '${locale.languageCode} primary screens render on large height and scaled text',
      (tester) async {
        final screens = <Widget>[
          const SplashScreen(),
          const MobileNumberScreen(),
          const OtpScreen(),
          const InboxScreen(),
        ];

        for (final screen in screens) {
          await tester.pumpWidget(
            _TestHost(
              width: 430,
              height: 900,
              locale: locale,
              textScale: 1.25,
              child: screen,
            ),
          );
          await tester.pump(const Duration(milliseconds: 50));
          expect(tester.takeException(), isNull);
        }
      },
    );
  }

  testWidgets('Arabic uses RTL and English uses LTR', (tester) async {
    await tester.pumpWidget(
      const _TestHost(width: 390, locale: Locale('ar'), child: InboxScreen()),
    );
    expect(
      find.descendant(
        of: find.byType(NavigationBar),
        matching: find.text('الوارد'),
      ),
      findsOneWidget,
    );
    expect(
      Directionality.of(tester.element(find.byType(InboxScreen))),
      TextDirection.rtl,
    );

    await tester.pumpWidget(
      const _TestHost(width: 390, locale: Locale('en'), child: InboxScreen()),
    );
    expect(
      find.descendant(
        of: find.byType(NavigationBar),
        matching: find.text('Inbox'),
      ),
      findsOneWidget,
    );
    expect(
      Directionality.of(tester.element(find.byType(InboxScreen))),
      TextDirection.ltr,
    );
  });

  testWidgets('Navigation shell exposes localized labels in Arabic', (
    tester,
  ) async {
    await tester.pumpWidget(
      const _TestHost(width: 390, locale: Locale('ar'), child: InboxScreen()),
    );

    expect(find.text('الرئيسية'), findsOneWidget);
    expect(find.text('الوارد'), findsWidgets);
    expect(find.text('الملف الشخصي'), findsOneWidget);
  });

  testWidgets('Navigation shell exposes localized labels in English', (
    tester,
  ) async {
    await tester.pumpWidget(
      const _TestHost(width: 390, locale: Locale('en'), child: InboxScreen()),
    );

    expect(find.text('Home'), findsOneWidget);
    expect(find.text('Inbox'), findsWidgets);
    expect(find.text('Profile'), findsOneWidget);
  });

  testWidgets('Sign in contains premium identity and official crest slot', (
    tester,
  ) async {
    await tester.pumpWidget(
      const _TestHost(
        width: 390,
        locale: Locale('ar'),
        child: MobileNumberScreen(),
      ),
    );
    await tester.pump(const Duration(milliseconds: 50));

    expect(find.text('الديوان الأميري'), findsOneWidget);
    expect(find.text('AL DIWAN AL AMIRI'), findsOneWidget);
    expect(find.byType(Image), findsOneWidget);
    expect(find.text('طلب رمز التحقق'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('Inbox empty state contains no fake delivery cards', (
    tester,
  ) async {
    await tester.pumpWidget(
      const _TestHost(width: 390, locale: Locale('ar'), child: InboxScreen()),
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

  test('Push destination is preserved through authentication', () {
    final navigation = AppNavigationState();

    expect(navigation.destinationForPush('delivery-42'), '/auth/mobile');
    expect(navigation.pendingDeliveryId, 'delivery-42');

    navigation.markOtpChallengeIssued();
    navigation.markOtpVerified();
    navigation.completeAuthentication();

    expect(navigation.isAuthenticated, isTrue);
    expect(
      navigation.postAuthenticationDestination(),
      '/delivery/delivery-42/login',
    );
  });

  test('Authenticated push routes directly to secure login', () {
    final navigation = AppNavigationState()..completeAuthentication();

    expect(
      navigation.destinationForPush('delivery 42'),
      '/delivery/delivery%2042/login',
    );
    expect(navigation.pendingDeliveryId, 'delivery 42');
  });

  test('Sign out clears authenticated and pending-delivery state', () {
    final navigation = AppNavigationState()
      ..completeAuthentication()
      ..rememberPendingDelivery('delivery-42')
      ..signOut();

    expect(navigation.isAuthenticated, isFalse);
    expect(navigation.pendingDeliveryId, isNull);
    expect(navigation.postAuthenticationDestination(), '/inbox');
  });
}

class _TestHost extends StatelessWidget {
  const _TestHost({
    required this.width,
    required this.locale,
    required this.child,
    this.height = 560,
    this.textScale = 1,
    this.viewInsetsBottom = 0,
  });

  final double width;
  final double height;
  final double textScale;
  final double viewInsetsBottom;
  final Locale locale;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      theme: DaSecureTheme.light,
      locale: locale,
      supportedLocales: const [Locale('ar'), Locale('en')],
      localizationsDelegates: GlobalMaterialLocalizations.delegates,
      builder: (context, child) {
        final media = MediaQuery.of(context);
        return MediaQuery(
          data: media.copyWith(
            textScaler: TextScaler.linear(textScale),
            viewInsets: EdgeInsets.only(bottom: viewInsetsBottom),
          ),
          child: child ?? const SizedBox.shrink(),
        );
      },
      home: Align(
        alignment: Alignment.topCenter,
        child: SizedBox(width: width, height: height, child: child),
      ),
    );
  }
}
