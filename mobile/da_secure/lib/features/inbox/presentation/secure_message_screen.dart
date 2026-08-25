import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/localization/da_strings.dart';
import 'package:da_secure/presentation/mobile_ui_contracts.dart';
import 'package:flutter/material.dart';
import 'package:flutter_html/flutter_html.dart';

class SecureMessageScreen extends StatelessWidget {
  const SecureMessageScreen({
    required this.deliveryId,
    this.state = const SecureMessageUiState(),
    this.onRetry,
    super.key,
  });

  final String deliveryId;
  final SecureMessageUiState state;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    final strings = DaStrings.of(context);

    return Scaffold(
      appBar: AppBar(title: Text(strings.secureMessage)),
      body: SafeArea(child: _buildBody(context, strings)),
    );
  }

  Widget _buildBody(BuildContext context, DaStrings strings) {
    switch (state.phase) {
      case SecureDeliveryUiPhase.loading:
      case SecureDeliveryUiPhase.submitting:
        return const Center(child: CircularProgressIndicator());
      case SecureDeliveryUiPhase.expired:
        return _StatusBody(message: strings.expired);
      case SecureDeliveryUiPhase.revoked:
        return _StatusBody(message: strings.revoked);
      case SecureDeliveryUiPhase.limitReached:
        return _StatusBody(message: strings.limitReached);
      case SecureDeliveryUiPhase.authenticationFailure:
        return _StatusBody(
          message: state.errorMessage ?? strings.authenticationFailed,
        );
      case SecureDeliveryUiPhase.error:
        return _StatusBody(
          message: state.errorMessage ?? strings.serviceUnavailable,
          actionLabel: onRetry == null ? null : strings.retry,
          onAction: onRetry,
        );
      case SecureDeliveryUiPhase.ready:
      case SecureDeliveryUiPhase.success:
        return ListView(
          padding: const EdgeInsets.fromLTRB(24, 20, 24, 32),
          children: [
            if (state.organizationName != null) ...[
              Text(
                state.organizationName!,
                textAlign: TextAlign.center,
                style: const TextStyle(
                  color: DaSecureColors.goldSoft,
                  fontSize: 18,
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 16),
            ],
            Text(
              strings.fixedMessageHeading,
              style: const TextStyle(fontSize: 21, fontWeight: FontWeight.w700),
            ),
            if (state.remainingRevealsLabel != null ||
                state.expiryLabel != null) ...[
              const SizedBox(height: 16),
              Container(
                padding: const EdgeInsets.all(14),
                decoration: BoxDecoration(
                  color: DaSecureColors.navy,
                  border: Border.all(color: DaSecureColors.gold),
                  borderRadius: BorderRadius.circular(14),
                ),
                child: Wrap(
                  spacing: 14,
                  runSpacing: 8,
                  children: [
                    if (state.remainingRevealsLabel != null)
                      Text(
                        '${strings.remainingReveals}: ${state.remainingRevealsLabel}',
                      ),
                    if (state.expiryLabel != null)
                      Text('${strings.expiresAt}: ${state.expiryLabel}'),
                  ],
                ),
              ),
            ],
            const SizedBox(height: 20),
            if (state.bodyHtml != null)
              Html(data: state.bodyHtml!)
            else
              SelectableText(
                state.bodyText ?? '',
                style: const TextStyle(fontSize: 16, height: 1.65),
              ),
            if (state.attachments.isNotEmpty) ...[
              const SizedBox(height: 24),
              Text(
                strings.attachments,
                style: const TextStyle(
                  color: DaSecureColors.goldSoft,
                  fontSize: 18,
                  fontWeight: FontWeight.w700,
                ),
              ),
              const SizedBox(height: 10),
              ...state.attachments.map(
                (attachment) => ListTile(
                  contentPadding: EdgeInsets.zero,
                  leading: const Icon(
                    Icons.attach_file,
                    color: DaSecureColors.gold,
                  ),
                  title: Text(attachment.name),
                  subtitle: attachment.sizeLabel == null
                      ? null
                      : Text(attachment.sizeLabel!),
                ),
              ),
            ],
          ],
        );
    }
  }
}

class _StatusBody extends StatelessWidget {
  const _StatusBody({required this.message, this.actionLabel, this.onAction});

  final String message;
  final String? actionLabel;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 420),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                message,
                textAlign: TextAlign.center,
                style: const TextStyle(color: DaSecureColors.textMuted),
              ),
              if (actionLabel != null && onAction != null) ...[
                const SizedBox(height: 14),
                OutlinedButton(onPressed: onAction, child: Text(actionLabel!)),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
