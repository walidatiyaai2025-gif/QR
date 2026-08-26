import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('DA Secure theme uses bundled Noto Sans Arabic centrally', () {
    final theme = DaSecureTheme.light;

    expect(DaSecureTheme.fontFamily, 'NotoSansArabic');
    expect(theme.textTheme.bodyMedium?.fontFamily, 'NotoSansArabic');
    expect(theme.textTheme.titleLarge?.fontFamily, 'NotoSansArabic');
  });

  testWidgets('mixed Arabic and Latin content inherits DA Secure typography', (
    tester,
  ) async {
    const mixedText = 'الديوان الأميري — DA Secure 1234 !?';

    await tester.pumpWidget(
      MaterialApp(
        theme: DaSecureTheme.light,
        home: const Scaffold(body: Text(mixedText)),
      ),
    );

    final context = tester.element(find.text(mixedText));
    expect(Theme.of(context).textTheme.bodyMedium?.fontFamily, 'NotoSansArabic');
    expect(find.text(mixedText), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
