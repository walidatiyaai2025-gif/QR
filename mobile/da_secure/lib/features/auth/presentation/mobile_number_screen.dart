import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/localization/da_strings.dart';
import 'package:da_secure/presentation/mobile_ui_contracts.dart';
import 'package:flutter/material.dart';

typedef RequestOtpCallback = Future<void> Function(String mobileNumber);

class MobileNumberScreen extends StatefulWidget {
  const MobileNumberScreen({
    this.state = const MobileNumberUiState(),
    this.onRequestOtp,
    super.key,
  });

  final MobileNumberUiState state;
  final RequestOtpCallback? onRequestOtp;

  @override
  State<MobileNumberScreen> createState() => _MobileNumberScreenState();
}

class _MobileNumberScreenState extends State<MobileNumberScreen> {
  final _formKey = GlobalKey<FormState>();
  final _mobileController = TextEditingController();

  @override
  void dispose() {
    _mobileController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) {
      return;
    }

    final callback = widget.onRequestOtp;
    if (callback == null) {
      final strings = DaStrings.of(context);
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(strings.serviceUnavailable)));
      return;
    }

    await callback(_mobileController.text.trim());
  }

  @override
  Widget build(BuildContext context) {
    final strings = DaStrings.of(context);

    return Scaffold(
      appBar: AppBar(backgroundColor: Colors.transparent),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(24, 12, 24, 32),
          children: [
            const SizedBox(height: 36),
            Text(
              strings.diwanArabic,
              textAlign: TextAlign.center,
              style: const TextStyle(
                color: DaSecureColors.goldSoft,
                fontSize: 18,
                fontWeight: FontWeight.w600,
              ),
            ),
            const SizedBox(height: 24),
            Text(
              strings.signIn,
              textAlign: TextAlign.center,
              style: const TextStyle(fontSize: 28, fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 8),
            Text(
              strings.mobilePrompt,
              textAlign: TextAlign.center,
              style: const TextStyle(color: DaSecureColors.textMuted),
            ),
            const SizedBox(height: 32),
            Form(
              key: _formKey,
              child: TextFormField(
                controller: _mobileController,
                enabled: !widget.state.isSubmitting,
                keyboardType: TextInputType.phone,
                textDirection: TextDirection.ltr,
                decoration: InputDecoration(
                  labelText: strings.mobileNumber,
                  prefixText: '+965 ',
                  hintText: '5555 1234',
                ),
                validator: (value) {
                  if (value == null || value.trim().isEmpty) {
                    return strings.mobileNumber;
                  }
                  return null;
                },
              ),
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
              onPressed: widget.state.isSubmitting ? null : _submit,
              child: widget.state.isSubmitting
                  ? const SizedBox.square(
                      dimension: 22,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : Text(strings.requestOtp),
            ),
          ],
        ),
      ),
    );
  }
}
