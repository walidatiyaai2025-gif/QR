import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/design_system/premium_visuals.dart';
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
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(strings.serviceUnavailable)));
      return;
    }

    await callback(_mobileController.text.trim());
  }

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
                const DaBrandIdentity(crestSize: 88, showAppName: false),
                const SizedBox(height: 22),
                Text(
                  strings.signIn,
                  textAlign: TextAlign.center,
                  style: const TextStyle(
                    fontSize: 29,
                    fontWeight: FontWeight.w800,
                    height: 1.2,
                  ),
                ),
                const SizedBox(height: 9),
                Text(
                  strings.mobilePrompt,
                  textAlign: TextAlign.center,
                  style: const TextStyle(
                    color: DaSecureColors.textMuted,
                    fontSize: 14,
                    height: 1.55,
                  ),
                ),
                const SizedBox(height: 24),
                DaPremiumCard(
                  child: Form(
                    key: _formKey,
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        TextFormField(
                          controller: _mobileController,
                          enabled: !widget.state.isSubmitting,
                          keyboardType: TextInputType.phone,
                          textInputAction: TextInputAction.done,
                          textDirection: TextDirection.ltr,
                          autofillHints: const [AutofillHints.telephoneNumber],
                          decoration: daPremiumInputDecoration(
                            labelText: strings.mobileNumber,
                            prefixIcon: const Icon(Icons.phone_iphone_rounded),
                            prefixText: '+965 ',
                            hintText: '5555 1234',
                          ),
                          validator: (value) {
                            if (value == null || value.trim().isEmpty) {
                              return strings.mobileNumber;
                            }
                            return null;
                          },
                          onFieldSubmitted: (_) {
                            if (!widget.state.isSubmitting) {
                              _submit();
                            }
                          },
                        ),
                        if (widget.state.errorMessage != null) ...[
                          const SizedBox(height: 12),
                          DaInlineError(message: widget.state.errorMessage!),
                        ],
                        const SizedBox(height: 18),
                        FilledButton(
                          onPressed: widget.state.isSubmitting ? null : _submit,
                          child: widget.state.isSubmitting
                              ? const SizedBox.square(
                                  dimension: 22,
                                  child: CircularProgressIndicator(
                                    strokeWidth: 2,
                                  ),
                                )
                              : Text(strings.requestOtp),
                        ),
                      ],
                    ),
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
