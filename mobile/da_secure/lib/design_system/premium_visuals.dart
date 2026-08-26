import 'dart:math' as math;

import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/localization/da_strings.dart';
import 'package:flutter/material.dart';

abstract final class DaResponsiveInsets {
  static double horizontal(BuildContext context) {
    final width = MediaQuery.sizeOf(context).width;
    if (width < 375) {
      return 20;
    }
    if (width < 430) {
      return 24;
    }
    return 28;
  }
}

class DaPremiumBackdrop extends StatelessWidget {
  const DaPremiumBackdrop({required this.child, super.key});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      decoration: const BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: <Color>[
            DaSecureColors.deepNavy,
            Color(0xFF082342),
            DaSecureColors.deepNavy,
          ],
          stops: <double>[0, 0.46, 1],
        ),
      ),
      child: child,
    );
  }
}

class DaBrandMark extends StatelessWidget {
  const DaBrandMark({this.size = 96, super.key});

  final double size;

  @override
  Widget build(BuildContext context) {
    return SizedBox.square(
      dimension: size,
      child: Image.asset(
        'assets/brand/diwan_crest.png',
        fit: BoxFit.contain,
        semanticLabel: DaStrings.of(context).diwanArabic,
        errorBuilder: (_, _, _) => SizedBox.square(dimension: size),
      ),
    );
  }
}

class DaBrandIdentity extends StatelessWidget {
  const DaBrandIdentity({
    this.crestSize = 96,
    this.showAppName = true,
    super.key,
  });

  final double crestSize;
  final bool showAppName;

  @override
  Widget build(BuildContext context) {
    final strings = DaStrings.of(context);

    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        DaBrandMark(size: crestSize),
        const SizedBox(height: 16),
        Text(
          strings.diwanArabic,
          textAlign: TextAlign.center,
          style: const TextStyle(
            color: DaSecureColors.textPrimary,
            fontSize: 24,
            fontWeight: FontWeight.w700,
            height: 1.25,
          ),
        ),
        const SizedBox(height: 5),
        Text(
          strings.diwanEnglish,
          textAlign: TextAlign.center,
          textDirection: TextDirection.ltr,
          style: const TextStyle(
            color: DaSecureColors.textMuted,
            fontSize: 12,
            fontWeight: FontWeight.w600,
            letterSpacing: 1.35,
          ),
        ),
        if (showAppName) ...[
          const SizedBox(height: 12),
          Text(
            strings.appName,
            textAlign: TextAlign.center,
            textDirection: TextDirection.ltr,
            style: const TextStyle(
              color: DaSecureColors.goldSoft,
              fontSize: 18,
              fontWeight: FontWeight.w700,
              letterSpacing: 0.35,
            ),
          ),
        ],
      ],
    );
  }
}

class DaPremiumCard extends StatelessWidget {
  const DaPremiumCard({
    required this.child,
    this.padding = const EdgeInsets.all(20),
    super.key,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: padding,
      decoration: BoxDecoration(
        color: DaSecureColors.navy,
        borderRadius: BorderRadius.circular(22),
        border: Border.all(color: DaSecureColors.border),
        boxShadow: const [
          BoxShadow(
            color: Color(0x33000000),
            blurRadius: 28,
            offset: Offset(0, 14),
          ),
        ],
      ),
      child: child,
    );
  }
}

class DaResponsivePage extends StatelessWidget {
  const DaResponsivePage({
    required this.child,
    this.maxWidth = 520,
    this.top = 20,
    this.bottom = 28,
    this.centerVertically = false,
    super.key,
  });

  final Widget child;
  final double maxWidth;
  final double top;
  final double bottom;
  final bool centerVertically;

  @override
  Widget build(BuildContext context) {
    final horizontal = DaResponsiveInsets.horizontal(context);
    final keyboardInset = MediaQuery.viewInsetsOf(context).bottom;

    return LayoutBuilder(
      builder: (context, constraints) {
        final minimumHeight = math.max(
          0.0,
          constraints.maxHeight - top - bottom - keyboardInset,
        );

        return SingleChildScrollView(
          keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
          padding: EdgeInsets.fromLTRB(
            horizontal,
            top,
            horizontal,
            bottom + keyboardInset,
          ),
          child: Center(
            child: ConstrainedBox(
              constraints: BoxConstraints(maxWidth: maxWidth),
              child: ConstrainedBox(
                constraints: BoxConstraints(minHeight: minimumHeight),
                child: centerVertically
                    ? Center(child: child)
                    : Align(alignment: Alignment.topCenter, child: child),
              ),
            ),
          ),
        );
      },
    );
  }
}

InputDecoration daPremiumInputDecoration({
  required String labelText,
  Widget? prefixIcon,
  Widget? suffixIcon,
  String? prefixText,
  String? hintText,
}) {
  return InputDecoration(
    labelText: labelText,
    prefixIcon: prefixIcon,
    suffixIcon: suffixIcon,
    prefixText: prefixText,
    hintText: hintText,
    filled: true,
    fillColor: DaSecureColors.deepNavy,
    contentPadding: const EdgeInsets.symmetric(horizontal: 18, vertical: 17),
    border: OutlineInputBorder(
      borderRadius: BorderRadius.circular(16),
      borderSide: const BorderSide(color: DaSecureColors.border),
    ),
    enabledBorder: OutlineInputBorder(
      borderRadius: BorderRadius.circular(16),
      borderSide: const BorderSide(color: DaSecureColors.border),
    ),
    focusedBorder: OutlineInputBorder(
      borderRadius: BorderRadius.circular(16),
      borderSide: const BorderSide(color: DaSecureColors.gold, width: 1.5),
    ),
  );
}

class DaInlineError extends StatelessWidget {
  const DaInlineError({required this.message, super.key});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Padding(
          padding: EdgeInsets.only(top: 2),
          child: Icon(
            Icons.error_outline_rounded,
            size: 18,
            color: Colors.redAccent,
          ),
        ),
        const SizedBox(width: 8),
        Expanded(
          child: Text(
            message,
            style: const TextStyle(
              color: Colors.redAccent,
              fontSize: 13,
              height: 1.4,
            ),
          ),
        ),
      ],
    );
  }
}
