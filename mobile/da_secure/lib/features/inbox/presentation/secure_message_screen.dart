import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/design_system/premium_visuals.dart';
import 'package:da_secure/localization/da_presentation_text.dart';
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
      body: DaPremiumBackdrop(
        child: SafeArea(top: false, child: _buildBody(context, strings)),
      ),
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
        final horizontal = DaResponsiveInsets.horizontal(context);
        final locale = Localizations.localeOf(context);

        return ListView(
          padding: EdgeInsets.fromLTRB(horizontal, 14, horizontal, 32),
          children: [
            Center(
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 680),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    if (state.organizationName != null) ...[
                      Row(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          const DaBrandMark(size: 42),
                          const SizedBox(width: 10),
                          Flexible(
                            child: Text(
                              state.organizationName!,
                              textAlign: TextAlign.center,
                              style: const TextStyle(
                                color: DaSecureColors.goldSoft,
                                fontSize: 17,
                                fontWeight: FontWeight.w700,
                              ),
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: 16),
                    ],
                    Text(
                      strings.fixedMessageHeading,
                      textAlign: TextAlign.start,
                      style: const TextStyle(
                        fontSize: 21,
                        fontWeight: FontWeight.w800,
                        height: 1.4,
                      ),
                    ),
                    if (state.remainingRevealsLabel != null ||
                        state.expiryLabel != null) ...[
                      const SizedBox(height: 16),
                      DaPremiumCard(
                        padding: const EdgeInsets.symmetric(
                          horizontal: 16,
                          vertical: 14,
                        ),
                        child: Wrap(
                          spacing: 10,
                          runSpacing: 10,
                          children: [
                            if (state.remainingRevealsLabel != null)
                              _InfoChip(
                                icon: Icons.visibility_outlined,
                                label: strings.remainingReveals,
                                value: DaPresentationText.isolateTechnical(
                                  state.remainingRevealsLabel!,
                                ),
                              ),
                            if (state.expiryLabel != null)
                              _InfoChip(
                                icon: Icons.schedule_rounded,
                                label: strings.expiresAt,
                                value: DaPresentationText.localizedRuntimeDate(
                                  state.expiryLabel!,
                                  locale,
                                ),
                              ),
                          ],
                        ),
                      ),
                    ],
                    const SizedBox(height: 18),
                    DaPremiumCard(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 18,
                        vertical: 20,
                      ),
                      child: state.bodyHtml != null
                          ? Html(data: state.bodyHtml!)
                          : SelectableText(
                              state.bodyText ?? '',
                              style: const TextStyle(
                                fontSize: 16,
                                height: 1.75,
                                color: DaSecureColors.textPrimary,
                              ),
                            ),
                    ),
                    if (state.attachments.isNotEmpty) ...[
                      const SizedBox(height: 24),
                      Text(
                        strings.attachments,
                        style: const TextStyle(
                          color: DaSecureColors.goldSoft,
                          fontSize: 18,
                          fontWeight: FontWeight.w800,
                        ),
                      ),
                      const SizedBox(height: 10),
                      ...state.attachments.map(
                        (attachment) => Padding(
                          padding: const EdgeInsets.only(bottom: 10),
                          child: Container(
                            decoration: BoxDecoration(
                              color: DaSecureColors.navy,
                              borderRadius: BorderRadius.circular(16),
                              border: Border.all(color: DaSecureColors.border),
                            ),
                            child: ListTile(
                              leading: const Icon(
                                Icons.attach_file_rounded,
                                color: DaSecureColors.gold,
                              ),
                              title: Text(
                                DaPresentationText.isolateDynamic(
                                  attachment.name,
                                ),
                                maxLines: 2,
                                overflow: TextOverflow.ellipsis,
                              ),
                              subtitle: attachment.sizeLabel == null
                                  ? null
                                  : Text(
                                      DaPresentationText.isolateTechnical(
                                        attachment.sizeLabel!,
                                      ),
                                      style: const TextStyle(
                                        color: DaSecureColors.textMuted,
                                      ),
                                    ),
                            ),
                          ),
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ),
          ],
        );
    }
  }
}

class _InfoChip extends StatelessWidget {
  const _InfoChip({
    required this.icon,
    required this.label,
    required this.value,
  });

  final IconData icon;
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
      decoration: BoxDecoration(
        color: DaSecureColors.deepNavy,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: DaSecureColors.border),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 17, color: DaSecureColors.goldSoft),
          const SizedBox(width: 7),
          Text(
            '$label: $value',
            style: const TextStyle(
              color: DaSecureColors.textPrimary,
              fontSize: 12.5,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ),
    );
  }
}

class _StatusBody extends StatelessWidget {
  const _StatusBody({required this.message, this.actionLabel, this.onAction});

  final String message;
  final String? actionLabel;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) {
    return DaResponsivePage(
      centerVertically: true,
      child: DaPremiumCard(
        padding: const EdgeInsets.symmetric(horizontal: 22, vertical: 26),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(
              Icons.info_outline_rounded,
              size: 36,
              color: DaSecureColors.goldSoft,
            ),
            const SizedBox(height: 14),
            Text(
              message,
              textAlign: TextAlign.center,
              style: const TextStyle(
                color: DaSecureColors.textMuted,
                height: 1.5,
              ),
            ),
            if (actionLabel != null && onAction != null) ...[
              const SizedBox(height: 16),
              OutlinedButton(onPressed: onAction, child: Text(actionLabel!)),
            ],
          ],
        ),
      ),
    );
  }
}
