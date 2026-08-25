import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/localization/da_strings.dart';
import 'package:da_secure/presentation/mobile_ui_contracts.dart';
import 'package:flutter/material.dart';

typedef SecureLoginCallback = Future<void> Function(
  String deliveryId,
  String username,
  String password,
);

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
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(strings.serviceUnavailable)),
      );
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
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(24, 24, 24, 32),
          children: [
            if (widget.state.organizationName != null) ...[
              Text(
                widget.state.organizationName!,
                textAlign: TextAlign.center,
                style: const TextStyle(
                  color: DaSecureColors.goldSoft,
                  fontSize: 17,
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 14),
            ],
            Text(
              strings.fixedMessageHeading,
              style: const TextStyle(
                fontSize: 20,
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(height: 20),
            if (terminalMessage != null)
              _StatusPanel(message: terminalMessage)
            else ...[
              TextField(
                controller: _usernameController,
                enabled: widget.state.phase != SecureDeliveryUiPhase.submitting,
                autocorrect: false,
                decoration: InputDecoration(labelText: strings.username),
              ),
              const SizedBox(height: 14),
              TextField(
                controller: _passwordController,
                enabled: widget.state.phase != SecureDeliveryUiPhase.submitting,
                obscureText: _obscurePassword,
                autocorrect: false,
                enableSuggestions: false,
                decoration: InputDecoration(
                  labelText: strings.password,
                  suffixIcon: IconButton(
                    onPressed: () {
                      setState(() => _obscurePassword = !_obscurePassword);
                    },
                    icon: Icon(
                      _obscurePassword
                          ? Icons.visibility_outlined
                          : Icons.visibility_off_outlined,
                    ),
                  ),
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
                onPressed:
                    widget.state.phase == SecureDeliveryUiPhase.submitting
                        ? null
                        : _submit,
                child: widget.state.phase == SecureDeliveryUiPhase.submitting
                    ? const SizedBox.square(
                        dimension: 22,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : Text(strings.signIn),
              ),
            ],
          ],
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
    return Container(
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: DaSecureColors.navy,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: DaSecureColors.border),
      ),
      child: Text(
        message,
        textAlign: TextAlign.center,
        style: const TextStyle(color: DaSecureColors.textMuted),
      ),
    );
  }
}
