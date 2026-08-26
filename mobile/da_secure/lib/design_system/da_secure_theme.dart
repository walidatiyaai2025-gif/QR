import 'package:flutter/material.dart';

abstract final class DaSecureColors {
  static const deepNavy = Color(0xFF031B36);
  static const navy = Color(0xFF08284B);
  static const gold = Color(0xFFD4A64A);
  static const goldSoft = Color(0xFFE4BE72);
  static const textPrimary = Color(0xFFF7F9FC);
  static const textMuted = Color(0xFFAAB7C9);
  static const border = Color(0xFF315173);
}

abstract final class DaSecureTheme {
  static const fontFamily = 'NotoSansArabic';

  static ThemeData get light {
    final scheme = ColorScheme.fromSeed(
      seedColor: DaSecureColors.gold,
      brightness: Brightness.dark,
      surface: DaSecureColors.deepNavy,
    );

    return ThemeData(
      useMaterial3: true,
      brightness: Brightness.dark,
      colorScheme: scheme,
      fontFamily: fontFamily,
      scaffoldBackgroundColor: DaSecureColors.deepNavy,
      appBarTheme: const AppBarTheme(
        backgroundColor: DaSecureColors.deepNavy,
        foregroundColor: DaSecureColors.textPrimary,
        centerTitle: false,
      ),
      inputDecorationTheme: const InputDecorationTheme(
        filled: true,
        fillColor: DaSecureColors.navy,
        border: OutlineInputBorder(),
        enabledBorder: OutlineInputBorder(
          borderSide: BorderSide(color: DaSecureColors.border),
        ),
        focusedBorder: OutlineInputBorder(
          borderSide: BorderSide(color: DaSecureColors.gold),
        ),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: DaSecureColors.gold,
          foregroundColor: DaSecureColors.deepNavy,
          minimumSize: const Size.fromHeight(52),
          textStyle: const TextStyle(fontWeight: FontWeight.w700),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          foregroundColor: DaSecureColors.goldSoft,
          side: const BorderSide(color: DaSecureColors.gold),
          minimumSize: const Size.fromHeight(48),
        ),
      ),
      navigationBarTheme: const NavigationBarThemeData(
        backgroundColor: DaSecureColors.navy,
        indicatorColor: DaSecureColors.gold,
        iconTheme: WidgetStatePropertyAll(
          IconThemeData(color: DaSecureColors.textPrimary),
        ),
      ),
    );
  }
}
