import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/design_system/premium_visuals.dart';
import 'package:da_secure/localization/da_presentation_text.dart';
import 'package:da_secure/localization/da_strings.dart';
import 'package:da_secure/presentation/mobile_ui_contracts.dart';
import 'package:flutter/material.dart';

typedef OpenDeliveryCallback = void Function(String deliveryId);

class InboxScreen extends StatelessWidget {
  const InboxScreen({
    this.state = const InboxUiState(),
    this.onRetry,
    this.onRefresh,
    this.onOpenDelivery,
    this.onLogout,
    super.key,
  });

  final InboxUiState state;
  final VoidCallback? onRetry;
  final Future<void> Function()? onRefresh;
  final OpenDeliveryCallback? onOpenDelivery;
  final Future<void> Function()? onLogout;

  @override
  Widget build(BuildContext context) {
    final strings = DaStrings.of(context);
    final horizontal = DaResponsiveInsets.horizontal(context);

    return Scaffold(
      appBar: AppBar(
        leadingWidth: 58,
        leading: const Padding(
          padding: EdgeInsetsDirectional.only(start: 16),
          child: Center(child: DaBrandMark(size: 34)),
        ),
        title: Text(
          state.organizationName ?? strings.appName,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
        ),
        actions: [
          if (onLogout != null)
            IconButton(
              tooltip: strings.signOut,
              onPressed: onLogout,
              icon: const Icon(Icons.logout_rounded),
            ),
        ],
      ),
      body: DaPremiumBackdrop(
        child: SafeArea(
          top: false,
          child: Padding(
            padding: EdgeInsetsDirectional.fromSTEB(
              horizontal,
              12,
              horizontal,
              0,
            ),
            child: Center(
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 680),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Row(
                      children: [
                        Container(
                          width: 38,
                          height: 38,
                          decoration: BoxDecoration(
                            color: DaSecureColors.navy,
                            borderRadius: BorderRadius.circular(12),
                            border: Border.all(color: DaSecureColors.border),
                          ),
                          alignment: Alignment.center,
                          child: const Icon(
                            Icons.lock_outline_rounded,
                            size: 20,
                            color: DaSecureColors.goldSoft,
                          ),
                        ),
                        const SizedBox(width: 12),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                strings.secureLinks,
                                style: const TextStyle(
                                  color: DaSecureColors.goldSoft,
                                  fontSize: 14,
                                  fontWeight: FontWeight.w700,
                                ),
                              ),
                              const SizedBox(height: 2),
                              Text(
                                strings.inboxSubtitle,
                                style: const TextStyle(
                                  color: DaSecureColors.textMuted,
                                  fontSize: 12.5,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 18),
                    Text(
                      strings.inbox,
                      style: const TextStyle(
                        fontSize: 30,
                        fontWeight: FontWeight.w800,
                        height: 1.15,
                      ),
                    ),
                    const SizedBox(height: 14),
                    Expanded(child: _buildBody(context, strings)),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
      bottomNavigationBar: NavigationBar(
        selectedIndex: 1,
        destinations: [
          NavigationDestination(
            icon: const Icon(Icons.home_outlined),
            selectedIcon: const Icon(Icons.home_rounded),
            label: strings.home,
          ),
          NavigationDestination(
            icon: const Icon(Icons.inbox_outlined),
            selectedIcon: const Icon(Icons.inbox_rounded),
            label: strings.inbox,
          ),
          NavigationDestination(
            icon: const Icon(Icons.person_outline_rounded),
            selectedIcon: const Icon(Icons.person_rounded),
            label: strings.profile,
          ),
        ],
      ),
    );
  }

  Widget _buildBody(BuildContext context, DaStrings strings) {
    switch (state.phase) {
      case UiPhase.loading:
        return const Center(child: CircularProgressIndicator());
      case UiPhase.error:
        return _CenteredState(
          icon: Icons.cloud_off_outlined,
          message: state.errorMessage ?? strings.serviceUnavailable,
          actionLabel: onRetry == null ? null : strings.retry,
          onAction: onRetry,
        );
      case UiPhase.success:
        if (state.items.isEmpty) {
          return _refreshableEmpty(strings);
        }
        final list = ListView.separated(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.only(bottom: 24),
          itemCount: state.items.length,
          separatorBuilder: (_, _) => const SizedBox(height: 12),
          itemBuilder: (context, index) {
            final item = state.items[index];
            return _InboxCard(
              item: item,
              heading: strings.fixedMessageHeading,
              sentLabel: strings.sentAt,
              expiryLabel: strings.expiresAt,
              remainingLabel: strings.remainingReveals,
              onTap: onOpenDelivery == null
                  ? null
                  : () => onOpenDelivery!(item.deliveryId),
            );
          },
        );
        return onRefresh == null
            ? list
            : RefreshIndicator(onRefresh: onRefresh!, child: list);
      case UiPhase.empty:
      case UiPhase.idle:
        return _refreshableEmpty(strings);
    }
  }

  Widget _refreshableEmpty(DaStrings strings) {
    final emptyState = _CenteredState(
      icon: Icons.mark_email_unread_outlined,
      message: strings.inboxEmpty,
    );

    if (onRefresh == null) {
      return emptyState;
    }
    return RefreshIndicator(
      onRefresh: onRefresh!,
      child: ListView(
        physics: const AlwaysScrollableScrollPhysics(),
        children: [SizedBox(height: 300, child: emptyState)],
      ),
    );
  }
}

class _CenteredState extends StatelessWidget {
  const _CenteredState({
    required this.icon,
    required this.message,
    this.actionLabel,
    this.onAction,
  });

  final IconData icon;
  final String message;
  final String? actionLabel;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 420),
        child: DaPremiumCard(
          padding: const EdgeInsets.symmetric(horizontal: 22, vertical: 24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(icon, color: DaSecureColors.goldSoft, size: 34),
              const SizedBox(height: 12),
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
      ),
    );
  }
}

class _InboxCard extends StatelessWidget {
  const _InboxCard({
    required this.item,
    required this.heading,
    required this.sentLabel,
    required this.expiryLabel,
    required this.remainingLabel,
    this.onTap,
  });

  final InboxDeliveryUiModel item;
  final String heading;
  final String sentLabel;
  final String expiryLabel;
  final String remainingLabel;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final locale = Localizations.localeOf(context);

    return Material(
      color: DaSecureColors.navy,
      shape: RoundedRectangleBorder(
        side: const BorderSide(color: DaSecureColors.border),
        borderRadius: BorderRadius.circular(20),
      ),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(18),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                width: 44,
                height: 44,
                decoration: BoxDecoration(
                  color: DaSecureColors.deepNavy,
                  borderRadius: BorderRadius.circular(14),
                  border: Border.all(color: DaSecureColors.border),
                ),
                alignment: Alignment.center,
                child: const Icon(
                  Icons.enhanced_encryption_outlined,
                  color: DaSecureColors.gold,
                  size: 23,
                ),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Text(
                      heading,
                      style: const TextStyle(
                        fontSize: 16.5,
                        fontWeight: FontWeight.w800,
                        height: 1.4,
                      ),
                    ),
                    if (item.remainingRevealsLabel != null) ...[
                      const SizedBox(height: 12),
                      _MetadataLine(
                        label: remainingLabel,
                        value: DaPresentationText.isolateTechnical(
                          item.remainingRevealsLabel!,
                        ),
                        highlighted: true,
                      ),
                    ],
                    if (item.sentLabel != null) ...[
                      const SizedBox(height: 7),
                      _MetadataLine(
                        label: sentLabel,
                        value: DaPresentationText.localizedRuntimeDate(
                          item.sentLabel!,
                          locale,
                        ),
                      ),
                    ],
                    if (item.expiryLabel != null) ...[
                      const SizedBox(height: 7),
                      _MetadataLine(
                        label: expiryLabel,
                        value: DaPresentationText.localizedRuntimeDate(
                          item.expiryLabel!,
                          locale,
                        ),
                      ),
                    ],
                  ],
                ),
              ),
              const SizedBox(width: 4),
              const Icon(
                Icons.chevron_right_rounded,
                color: DaSecureColors.textMuted,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _MetadataLine extends StatelessWidget {
  const _MetadataLine({
    required this.label,
    required this.value,
    this.highlighted = false,
  });

  final String label;
  final String value;
  final bool highlighted;

  @override
  Widget build(BuildContext context) {
    return Text.rich(
      TextSpan(
        children: [
          TextSpan(
            text: '$label: ',
            style: const TextStyle(
              color: DaSecureColors.textMuted,
              fontWeight: FontWeight.w600,
            ),
          ),
          TextSpan(
            text: value,
            style: TextStyle(
              color: highlighted
                  ? DaSecureColors.goldSoft
                  : DaSecureColors.textPrimary,
              fontWeight: highlighted ? FontWeight.w700 : FontWeight.w500,
            ),
          ),
        ],
      ),
      style: const TextStyle(fontSize: 12.5, height: 1.35),
    );
  }
}
