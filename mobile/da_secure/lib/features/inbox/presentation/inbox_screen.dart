import 'package:da_secure/design_system/da_secure_theme.dart';
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

    return Scaffold(
      appBar: AppBar(
        title: Text(state.organizationName ?? strings.appName),
        centerTitle: true,
        actions: [
          if (onLogout != null)
            IconButton(
              tooltip: strings.signOut,
              onPressed: onLogout,
              icon: const Icon(Icons.logout),
            ),
        ],
      ),
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 12, 20, 0),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                strings.secureLinks,
                textAlign: TextAlign.center,
                style: const TextStyle(
                  color: DaSecureColors.goldSoft,
                  fontSize: 16,
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 18),
              Text(
                strings.inbox,
                style: const TextStyle(
                  fontSize: 28,
                  fontWeight: FontWeight.w700,
                ),
              ),
              const SizedBox(height: 14),
              Expanded(child: _buildBody(context, strings)),
            ],
          ),
        ),
      ),
      bottomNavigationBar: NavigationBar(
        selectedIndex: 1,
        destinations: const [
          NavigationDestination(icon: Icon(Icons.home_outlined), label: ''),
          NavigationDestination(icon: Icon(Icons.inbox_outlined), label: ''),
          NavigationDestination(icon: Icon(Icons.person_outline), label: ''),
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
          itemCount: state.items.length,
          separatorBuilder: (_, __) => const SizedBox(height: 12),
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
    if (onRefresh == null) {
      return _CenteredState(message: strings.inboxEmpty);
    }
    return RefreshIndicator(
      onRefresh: onRefresh!,
      child: ListView(
        physics: const AlwaysScrollableScrollPhysics(),
        children: [
          SizedBox(
            height: 320,
            child: _CenteredState(message: strings.inboxEmpty),
          ),
        ],
      ),
    );
  }
}

class _CenteredState extends StatelessWidget {
  const _CenteredState({
    required this.message,
    this.actionLabel,
    this.onAction,
  });

  final String message;
  final String? actionLabel;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) {
    return Center(
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
    return Material(
      color: DaSecureColors.navy,
      shape: RoundedRectangleBorder(
        side: const BorderSide(color: DaSecureColors.border),
        borderRadius: BorderRadius.circular(18),
      ),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(18),
        child: Padding(
          padding: const EdgeInsets.all(18),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                heading,
                style: const TextStyle(
                  fontSize: 17,
                  fontWeight: FontWeight.w700,
                ),
              ),
              if (item.remainingRevealsLabel != null) ...[
                const SizedBox(height: 12),
                Text(
                  '$remainingLabel: ${item.remainingRevealsLabel}',
                  style: const TextStyle(color: DaSecureColors.goldSoft),
                ),
              ],
              if (item.sentLabel != null) ...[
                const SizedBox(height: 6),
                Text(
                  '$sentLabel: ${item.sentLabel}',
                  style: const TextStyle(color: DaSecureColors.textMuted),
                ),
              ],
              if (item.expiryLabel != null) ...[
                const SizedBox(height: 6),
                Text(
                  '$expiryLabel: ${item.expiryLabel}',
                  style: const TextStyle(color: DaSecureColors.textMuted),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
