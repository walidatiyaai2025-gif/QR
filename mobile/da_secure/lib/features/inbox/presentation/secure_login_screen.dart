import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/design_system/premium_visuals.dart';
import 'package:da_secure/localization/da_strings.dart';
import 'package:da_secure/presentation/mobile_ui_contracts.dart';
import 'package:flutter/material.dart';

typedef SecureLoginCallback =
    Future<void> Function(String deliveryId, String username, String password);

class SecureLoginScreen extends StatefulWidget {
  const SecureLoginScreen({
    required this.deliveryId,
    this.state = const SecureLoginUiState(),
    this.onAuthenticate,
    super.key,
  });

  final String deliveryId;
  final SecureLoginUiState state;
  final SecureLoginCallback? onAuthenticate;

  @override
  State<SecureLoginScreen> createState() => _SecureLoginScreenState();
}

class _SecureLoginScreenState extends State<SecureLoginScreen> {
  final _usernameController = TextEditingController();
  final _passwordController = TextEditingController();
  bool _obscurePassword = true;

  @override
  void dispose() {
    _usernameController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final callback = widget.onAuthenticate;
    final strings = DaStrings.of(context);

    if (callback == null) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(strings.serviceUnavailable)));
      return;
    }

    await callback(
      widget.deliveryId,
      _usernameController.text,
      _passwordController.text,
    );
  }

  @override
  Widget build(BuildContext context) {
    final strings = DaStrings.of(context);
    final terminalMessage = _terminalMessage(strings);

    return Scaffold(
      appBar: AppBar(title: Text(strings.secureMessageLogin)),
      body: DaPremiumBackdrop(
        child: SafeArea(
          top: false,
          child: DaResponsivePage(
            centerVertically: true,
            top: 10,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const DaBrandMark(size: 70),
                if (widget.state.organizationName != null) ...[
                  const SizedBox(height: 12),
                  Text(
                    widget.state.organizationName!,
                    textAlign: TextAlign.center,
                    style: const TextStyle(
                      color: DaSecureColors.goldSoft,
                      fontSize: 16,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ],
                const SizedBox(height: 18),
                Text(
                  strings.fixedMessageHeading,
                  textAlign: TextAlign.center,
                  style: const TextStyle(
                    fontSize: 21,
                    fontWeight: FontWeight.w800,
                    height: 1.4,
                  ),
                ),
                const SizedBox(height: 8),
                Text(
                  strings.secureAccessSubtitle,
                  textAlign: TextAlign.center,
                  style: const TextStyle(
                    color: DaSecureColors.textMuted,
                    fontSize: 13.5,
                    height: 1.5,
                  ),
                ),
                const SizedBox(height: 22),
                if (terminalMessage != null)
                  _StatusPanel(message: terminalMessage)
                else
                  DaPremiumCard(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        TextField(
                          controller: _usernameController,
                          enabled:
                              widget.state.phase !=
                              SecureDeliveryUiPhase.submitting,
                          textInputAction: TextInputAction.next,
                          autocorrect: false,
                          enableSuggestions: false,
                          decoration: daPremiumInputDecoration(
                            labelText: strings.username,
                            prefixIcon: const Icon(Icons.person_outline_rounded),
                          ),
                        ),
                        const SizedBox(height: 14),
                        TextField(
                          controller: _passwordController,
                          enabled:
                              widget.state.phase !=
                              SecureDeliveryUiPhase.submitting,
                          obscureText: _obscurePassword,
                          textInputAction: TextInputAction.done,
                          autocorrect: false,
                          enableSuggestions: false,
                          decoration: daPremiumInputDecoration(
                            labelText: strings.password,
                            prefixIcon: const Icon(Icons.lock_outline_rounded),
                            suffixIcon: IconButton(
                              onPressed: () {
                                setState(
                                  () => _obscurePassword = !_obscurePassword,
                                );
                              },
                              icon: Icon(
                                _obscurePassword
                                    ? Icons.visibility_outlined
                                    : Icons.visibility_off_outlined,
                              ),
                            ),
                          ),
                          onSubmitted: (_) {
                            if (widget.state.phase !=
                                SecureDeliveryUiPhase.submitting) {
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
                          onPressed:
                              widget.state.phase ==
                                  SecureDeliveryUiPhase.submitting
                              ? null
                              : _submit,
                          child:
                              widget.state.phase ==
                                  SecureDeliveryUiPhase.submitting
                              ? const SizedBox.square(
                                  dimension: 22,
                                  child: CircularProgressIndicator(
                                    strokeWidth: 2,
                                  ),
                                )
                              : Text(strings.signIn),
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

  String? _terminalMessage(DaStrings strings) {
    switch (widget.state.phase) {
      case SecureDeliveryUiPhase.expired:
        return strings.expired;
      case SecureDeliveryUiPhase.revoked:
        return strings.revoked;
      case SecureDeliveryUiPhase.limitReached:
        return strings.limitReached;
      case SecureDeliveryUiPhase.authenticationFailure:
        return widget.state.errorMessage ?? strings.authenticationFailed;
      case SecureDeliveryUiPhase.error:
        return widget.state.errorMessage ?? strings.serviceUnavailable;
      case SecureDeliveryUiPhase.loading:
        return strings.loading;
      case SecureDeliveryUiPhase.ready:
      case SecureDeliveryUiPhase.submitting:
      case SecureDeliveryUiPhase.success:
        return null;
    }
  }
}

class _StatusPanel extends StatelessWidget {
  const _StatusPanel({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return DaPremiumCard(
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 24),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(
            Icons.info_outline_rounded,
            color: DaSecureColors.goldSoft,
            size: 34,
          ),
          const SizedBox(height: 12),
          Text(
            message,
            textAlign: TextAlign.center,
            style: const TextStyle(
              color: DaSecureColors.textMuted,
              height: 1.5,
            ),
          ),
        ],
      ),
    );
  }
}
