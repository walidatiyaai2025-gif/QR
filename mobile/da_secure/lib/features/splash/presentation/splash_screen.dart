import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/design_system/premium_visuals.dart';
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
      body: DaPremiumBackdrop(
        child: SafeArea(
          child: DaResponsivePage(
            centerVertically: true,
            top: 24,
            bottom: 24,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const DaBrandIdentity(crestSize: 132),
                const SizedBox(height: 32),
                if (isLoading) ...[
                  SizedBox(
                    width: 180,
                    child: ClipRRect(
                      borderRadius: BorderRadius.circular(20),
                      child: const LinearProgressIndicator(
                        minHeight: 3,
                        backgroundColor: DaSecureColors.border,
                        color: DaSecureColors.gold,
                      ),
                    ),
                  ),
                  const SizedBox(height: 12),
                  Text(
                    strings.loading,
                    textAlign: TextAlign.center,
                    style: const TextStyle(
                      color: DaSecureColors.textMuted,
                      fontSize: 12.5,
                    ),
                  ),
                ] else if (errorMessage != null) ...[
                  DaPremiumCard(
                    padding: const EdgeInsets.all(18),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        const Icon(
                          Icons.cloud_off_outlined,
                          color: DaSecureColors.goldSoft,
                          size: 30,
                        ),
                        const SizedBox(height: 10),
                        Text(
                          errorMessage!,
                          textAlign: TextAlign.center,
                          style: const TextStyle(
                            color: DaSecureColors.textMuted,
                            height: 1.5,
                          ),
                        ),
                        if (onRetry != null) ...[
                          const SizedBox(height: 16),
                          OutlinedButton(
                            onPressed: onRetry,
                            child: Text(strings.retry),
                          ),
                        ],
                      ],
                    ),
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}
