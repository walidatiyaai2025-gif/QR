import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/localization/da_strings.dart';
import 'package:da_secure/presentation/mobile_ui_contracts.dart';
import 'package:flutter/material.dart';

class BiometricScreen extends StatelessWidget {
  const BiometricScreen({
    this.state = const BiometricUiState(),
    this.onEnable,
    this.onSkip,
    super.key,
  });

  final BiometricUiState state;
  final Future<void> Function()? onEnable;
  final Future<void> Function()? onSkip;

  @override
  Widget build(BuildContext context) {
    final strings = DaStrings.of(context);

    return Scaffold(
      appBar: AppBar(backgroundColor: Colors.transparent),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(24, 24, 24, 32),
          children: [
            const SizedBox(height: 20),
            Container(
              padding: const EdgeInsets.all(24),
              decoration: BoxDecoration(
                color: DaSecureColors.navy,
                border: Border.all(color: DaSecureColors.border),
                borderRadius: BorderRadius.circular(20),
              ),
              child: Column(
                children: [
                  const Icon(
                    Icons.fingerprint,
                    size: 72,
                    color: DaSecureColors.gold,
                  ),
                  const SizedBox(height: 20),
                  Text(
                    strings.biometricTitle,
                    textAlign: TextAlign.center,
                    style: const TextStyle(
                      fontSize: 26,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  const SizedBox(height: 10),
                  Text(
                    strings.biometricBody,
                    textAlign: TextAlign.center,
                    style: const TextStyle(color: DaSecureColors.textMuted),
                  ),
                ],
              ),
            ),
            if (state.errorMessage != null) ...[
              const SizedBox(height: 12),
              Text(
                state.errorMessage!,
                style: const TextStyle(color: Colors.redAccent),
              ),
            ],
            const SizedBox(height: 24),
            FilledButton(
              onPressed: state.isBusy || onEnable == null ? null : onEnable,
              child: Text(strings.enableBiometric),
            ),
            const SizedBox(height: 10),
            TextButton(
              onPressed: state.isBusy || onSkip == null ? null : onSkip,
              child: Text(strings.notNow),
            ),
          ],
        ),
      ),
    );
  }
}
