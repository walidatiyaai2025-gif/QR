import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/localization/da_strings.dart';
import 'package:flutter/material.dart';

class SplashScreen extends StatelessWidget {
  const SplashScreen({
    this.isLoading = true,
    this.errorMessage,
    this.onRetry,
    super.key,
  });

  final bool isLoading;
  final String? errorMessage;
  final Future<void> Function()? onRetry;

  @override
  Widget build(BuildContext context) {
    final strings = DaStrings.of(context);

    return Scaffold(
      body: SafeArea(
        child: Center(
          child: Padding(
            padding: const EdgeInsets.all(32),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 460),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const SizedBox(height: 108),
                  Text(
                    strings.diwanArabic,
                    textAlign: TextAlign.center,
                    style: const TextStyle(
                      fontSize: 30,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  const SizedBox(height: 6),
                  Text(
                    strings.diwanEnglish,
                    textAlign: TextAlign.center,
                    style: const TextStyle(
                      color: DaSecureColors.textMuted,
                      fontSize: 13,
                      letterSpacing: 1.2,
                    ),
                  ),
                  const SizedBox(height: 20),
                  Text(
                    strings.appName,
                    textAlign: TextAlign.center,
                    style: const TextStyle(
                      color: DaSecureColors.goldSoft,
                      fontSize: 19,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  const SizedBox(height: 28),
                  if (isLoading)
                    const CircularProgressIndicator()
                  else if (errorMessage != null) ...[
                    Text(
                      errorMessage!,
                      textAlign: TextAlign.center,
                      style: const TextStyle(color: DaSecureColors.textMuted),
                    ),
                    if (onRetry != null) ...[
                      const SizedBox(height: 16),
                      OutlinedButton(
                        onPressed: onRetry,
                        child: Text(strings.retry),
                      ),
                    ],
                  ],
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
