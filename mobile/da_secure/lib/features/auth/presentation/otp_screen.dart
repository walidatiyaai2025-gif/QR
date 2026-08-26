import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/design_system/premium_visuals.dart';
import 'package:da_secure/localization/da_strings.dart';
import 'package:da_secure/presentation/mobile_ui_contracts.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

typedef VerifyOtpCallback = Future<void> Function(String otp);
typedef ResendOtpCallback = Future<void> Function();

class OtpScreen extends StatefulWidget {
  const OtpScreen({
    this.state = const OtpUiState(),
    this.onVerify,
    this.onResend,
    super.key,
  });

  final OtpUiState state;
  final VerifyOtpCallback? onVerify;
  final ResendOtpCallback? onResend;

  @override
  State<OtpScreen> createState() => _OtpScreenState();
}

class _OtpScreenState extends State<OtpScreen> {
  final _otpController = TextEditingController();

  @override
  void dispose() {
    _otpController.dispose();
    super.dispose();
  }

  Future<void> _verify() async {
    final strings = DaStrings.of(context);
    final otp = _otpController.text.trim();

    if (otp.length != 6) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(strings.otpPrompt)));
      return;
    }

    final callback = widget.onVerify;
    if (callback == null) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(strings.serviceUnavailable)));
      return;
    }

    await callback(otp);
  }

  @override
  Widget build(BuildContext context) {
    final strings = DaStrings.of(context);
    final canResend =
        !widget.state.isSubmitting && widget.state.resendSeconds <= 0;

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
                const DaBrandMark(size: 78),
                const SizedBox(height: 18),
                Text(
                  strings.verifyCode,
                  textAlign: TextAlign.center,
                  style: const TextStyle(
                    fontSize: 28,
                    fontWeight: FontWeight.w800,
                  ),
                ),
                const SizedBox(height: 8),
                Text(
                  strings.otpPrompt,
                  textAlign: TextAlign.center,
                  style: const TextStyle(
                    color: DaSecureColors.textMuted,
                    fontSize: 14,
                    height: 1.5,
                  ),
                ),
                const SizedBox(height: 24),
                DaPremiumCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      TextField(
                        controller: _otpController,
                        enabled: !widget.state.isSubmitting,
                        keyboardType: TextInputType.number,
                        textInputAction: TextInputAction.done,
                        textDirection: TextDirection.ltr,
                        textAlign: TextAlign.center,
                        maxLength: 6,
                        inputFormatters: [
                          FilteringTextInputFormatter.digitsOnly,
                          LengthLimitingTextInputFormatter(6),
                        ],
                        style: const TextStyle(
                          fontSize: 27,
                          letterSpacing: 10,
                          fontWeight: FontWeight.w700,
                        ),
                        decoration: daPremiumInputDecoration(
                          labelText: strings.verifyCode,
                          prefixIcon: const Icon(Icons.password_rounded),
                        ).copyWith(counterText: ''),
                        onSubmitted: (_) {
                          if (!widget.state.isSubmitting) {
                            _verify();
                          }
                        },
                      ),
                      const SizedBox(height: 12),
                      Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          const Icon(
                            Icons.shield_outlined,
                            size: 17,
                            color: DaSecureColors.goldSoft,
                          ),
                          const SizedBox(width: 8),
                          Expanded(
                            child: Text(
                              strings.otpSecurityNote,
                              style: const TextStyle(
                                color: DaSecureColors.textMuted,
                                fontSize: 12.5,
                                height: 1.45,
                              ),
                            ),
                          ),
                        ],
                      ),
                      if (widget.state.errorMessage != null) ...[
                        const SizedBox(height: 12),
                        DaInlineError(message: widget.state.errorMessage!),
                      ],
                      const SizedBox(height: 18),
                      FilledButton(
                        onPressed: widget.state.isSubmitting ? null : _verify,
                        child: widget.state.isSubmitting
                            ? const SizedBox.square(
                                dimension: 22,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              )
                            : Text(strings.verify),
                      ),
                      const SizedBox(height: 6),
                      TextButton(
                        onPressed: canResend && widget.onResend != null
                            ? widget.onResend
                            : null,
                        child: Text(
                          widget.state.resendSeconds > 0
                              ? strings.resendIn(widget.state.resendSeconds)
                              : strings.resend,
                        ),
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
