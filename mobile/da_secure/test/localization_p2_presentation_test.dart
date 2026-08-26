import 'package:da_secure/features/inbox/presentation/inbox_screen.dart';
import 'package:da_secure/features/inbox/presentation/secure_message_screen.dart';
import 'package:da_secure/localization/da_presentation_text.dart';
import 'package:da_secure/presentation/mobile_ui_contracts.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('runtime dates are locale-aware and bidi isolated', () {
    expect(
      DaPresentationText.localizedRuntimeDate(
        '2026-08-26 14:05',
        const Locale('ar'),
      ),
      '\u206626/08/2026 14:05\u2069',
    );
    expect(
      DaPresentationText.localizedRuntimeDate(
        '2026-08-26 14:05',
        const Locale('en'),
      ),
      '\u206608/26/2026 14:05\u2069',
    );
    expect(
      DaPresentationText.isolateDynamic('invoice-فاتورة-2026.pdf'),
      '\u2068invoice-فاتورة-2026.pdf\u2069',
    );
  });

  testWidgets('Arabic inbox closes P2 labels metadata and target widths', (
    tester,
  ) async {
    for (final width in <double>[360, 375, 390, 412, 430]) {
      await _pumpInbox(tester, const Locale('ar'), width);

      expect(find.text('الرئيسية'), findsOneWidget);
      expect(find.text('الوارد'), findsWidgets);
      expect(find.text('الملف الشخصي'), findsOneWidget);
      expect(
        find.textContaining('\u206626/08/2026 14:05\u2069'),
        findsOneWidget,
      );
      expect(
        Directionality.of(tester.element(find.byType(InboxScreen))),
        TextDirection.rtl,
      );
      expect(tester.takeException(), isNull);
    }

    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });

  testWidgets('English inbox closes P2 labels metadata and target widths', (
    tester,
  ) async {
    for (final width in <double>[390, 430]) {
      await _pumpInbox(tester, const Locale('en'), width);

      expect(find.text('Home'), findsOneWidget);
      expect(find.text('Inbox'), findsWidgets);
      expect(find.text('Profile'), findsOneWidget);
      expect(
        find.textContaining('\u206608/26/2026 14:05\u2069'),
        findsOneWidget,
      );
      expect(
        Directionality.of(tester.element(find.byType(InboxScreen))),
        TextDirection.ltr,
      );
      expect(tester.takeException(), isNull);
    }

    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });

  testWidgets('secure message bidi isolates attachment technical strings', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(390, 844);

    await tester.pumpWidget(
      _localizedApp(
        locale: const Locale('ar'),
        home: const SecureMessageScreen(
          deliveryId: '42',
          state: SecureMessageUiState(
            phase: SecureDeliveryUiPhase.success,
            expiryLabel: '2026-08-26 14:05',
            remainingRevealsLabel: '3',
            bodyText: 'رسالة آمنة',
            attachments: <AttachmentUiModel>[
              AttachmentUiModel(
                name: 'invoice-فاتورة-2026.pdf',
                sizeLabel: '1.2 MB',
              ),
            ],
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('\u2068invoice-فاتورة-2026.pdf\u2069'), findsOneWidget);
    expect(find.text('\u20661.2 MB\u2069'), findsOneWidget);
    expect(
      find.textContaining('\u206626/08/2026 14:05\u2069'),
      findsOneWidget,
    );
    expect(tester.takeException(), isNull);

    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
}

Future<void> _pumpInbox(
  WidgetTester tester,
  Locale locale,
  double width,
) async {
  tester.view.devicePixelRatio = 1;
  tester.view.physicalSize = Size(width, 844);

  await tester.pumpWidget(
    _localizedApp(
      locale: locale,
      home: const InboxScreen(
        state: InboxUiState(
          phase: UiPhase.success,
          organizationName: 'DA Secure',
          items: <InboxDeliveryUiModel>[
            InboxDeliveryUiModel(
              deliveryId: '42',
              sentLabel: '2026-08-26 14:05',
              expiryLabel: '2026-08-27 09:30',
              remainingRevealsLabel: '3',
            ),
          ],
        ),
      ),
    ),
  );
  await tester.pumpAndSettle();
}

Widget _localizedApp({required Locale locale, required Widget home}) {
  return MaterialApp(
    locale: locale,
    supportedLocales: const <Locale>[Locale('ar'), Locale('en')],
    localizationsDelegates: GlobalMaterialLocalizations.delegates,
    home: home,
  );
}
