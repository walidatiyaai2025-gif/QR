import 'package:da_secure/design_system/da_secure_theme.dart';
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
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(strings.otpPrompt)));
      return;
    }

    final callback = widget.onVerify;
    if (callback == null) {
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(strings.serviceUnavailable)));
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
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(24, 24, 24, 32),
          children: [
            Text(
              strings.verifyCode,
              style: const TextStyle(fontSize: 28, fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 8),
            Text(
              strings.otpPrompt,
              style: const TextStyle(color: DaSecureColors.textMuted),
            ),
            const SizedBox(height: 28),
            TextField(
              controller: _otpController,
              enabled: !widget.state.isSubmitting,
              keyboardType: TextInputType.number,
              textDirection: TextDirection.ltr,
              textAlign: TextAlign.center,
              maxLength: 6,
              inputFormatters: [
                FilteringTextInputFormatter.digitsOnly,
                LengthLimitingTextInputFormatter(6),
              ],
              style: const TextStyle(
                fontSize: 28,
                letterSpacing: 12,
                fontWeight: FontWeight.w600,
              ),
              decoration: const InputDecoration(counterText: ''),
            ),
            if (widget.state.errorMessage != null) ...[
              const SizedBox(height: 12),
              Text(
                widget.state.errorMessage!,
                style: const TextStyle(color: Colors.redAccent),
              ),
            ],
            const SizedBox(height: 20),
            FilledButton(
              onPressed: widget.state.isSubmitting ? null : _verify,
              child: widget.state.isSubmitting
                  ? const SizedBox.square(
                      dimension: 22,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : Text(strings.verify),
            ),
            const SizedBox(height: 12),
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
    );
  }
}
