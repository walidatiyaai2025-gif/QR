import 'package:flutter/widgets.dart';

abstract final class DaPresentationText {
  static const String _lri = '\u2066';
  static const String _fsi = '\u2068';
  static const String _pdi = '\u2069';

  static String isolateTechnical(String value) => '$_lri$value$_pdi';

  static String isolateDynamic(String value) => '$_fsi$value$_pdi';

  static String localizedRuntimeDate(String value, Locale locale) {
    final parsed = DateTime.tryParse(value.replaceFirst(' ', 'T'));
    if (parsed == null) return isolateTechnical(value);

    String two(int number) => number.toString().padLeft(2, '0');
    final date = locale.languageCode.toLowerCase() == 'ar'
        ? '${two(parsed.day)}/${two(parsed.month)}/${parsed.year} ${two(parsed.hour)}:${two(parsed.minute)}'
        : '${two(parsed.month)}/${two(parsed.day)}/${parsed.year} ${two(parsed.hour)}:${two(parsed.minute)}';

    return isolateTechnical(date);
  }
}
