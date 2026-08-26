import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/design_system/premium_visuals.dart';
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
      body: DaPremiumBackdrop(
        child: SafeArea(
          top: false,
          child: DaResponsivePage(
            centerVertically: true,
            top: 8,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const DaBrandMark(size: 72),
                const SizedBox(height: 18),
                DaPremiumCard(
                  child: Column(
                    children: [
                      Container(
                        width: 92,
                        height: 92,
                        decoration: BoxDecoration(
                          color: DaSecureColors.deepNavy,
                          borderRadius: BorderRadius.circular(28),
                          border: Border.all(color: DaSecureColors.gold),
                        ),
                        alignment: Alignment.center,
                        child: const Icon(
                          Icons.fingerprint_rounded,
                          size: 58,
                          color: DaSecureColors.gold,
                        ),
                      ),
                      const SizedBox(height: 22),
                      Text(
                        strings.biometricTitle,
                        textAlign: TextAlign.center,
                        style: const TextStyle(
                          fontSize: 26,
                          fontWeight: FontWeight.w800,
                          height: 1.25,
                        ),
                      ),
                      const SizedBox(height: 10),
                      Text(
                        strings.biometricBody,
                        textAlign: TextAlign.center,
                        style: const TextStyle(
                          color: DaSecureColors.textMuted,
                          height: 1.55,
                        ),
                      ),
                      if (state.errorMessage != null) ...[
                        const SizedBox(height: 14),
                        DaInlineError(message: state.errorMessage!),
                      ],
                      const SizedBox(height: 22),
                      FilledButton(
                        onPressed: state.isBusy || onEnable == null
                            ? null
                            : onEnable,
                        child: state.isBusy
                            ? const SizedBox.square(
                                dimension: 22,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              )
                            : Text(strings.enableBiometric),
                      ),
                      const SizedBox(height: 6),
                      TextButton(
                        onPressed: state.isBusy || onSkip == null
                            ? null
                            : onSkip,
                        child: Text(strings.notNow),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
