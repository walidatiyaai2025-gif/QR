import 'package:da_secure/features/auth/presentation/biometric_screen.dart';
import 'package:da_secure/features/auth/presentation/mobile_number_screen.dart';
import 'package:da_secure/features/auth/presentation/otp_screen.dart';
import 'package:da_secure/features/inbox/presentation/inbox_screen.dart';
import 'package:da_secure/features/inbox/presentation/secure_login_screen.dart';
import 'package:da_secure/features/inbox/presentation/secure_message_screen.dart';
import 'package:da_secure/features/splash/presentation/splash_screen.dart';
import 'package:da_secure/presentation/mobile_ui_contracts.dart';
import 'package:da_secure/routing/app_navigation_state.dart';
import 'package:da_secure/runtime/app_runtime.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

GoRouter createAppRouter(AppRuntime runtime) => GoRouter(
  initialLocation: '/',
  refreshListenable: runtime,
  redirect: (_, state) {
    final location = state.matchedLocation;

    if (runtime.isBooting || runtime.bootFailure != null) {
      return location == '/' ? null : '/';
    }

    if (location == '/') {
      if (runtime.isAuthenticated) {
        return runtime.postAuthenticationDestination();
      }
      return switch (runtime.stage) {
        MobileAuthStage.otp => '/auth/otp',
        MobileAuthStage.biometricOffer => '/auth/biometric',
        _ => '/auth/mobile',
      };
    }

    final isProtected =
        location == '/inbox' || location.startsWith('/delivery/');
    if (isProtected && !runtime.isAuthenticated) {
      return '/auth/mobile';
    }

    if (runtime.isAuthenticated && runtime.pendingDeliveryId != null) {
      final destination = runtime.postAuthenticationDestination();
      if (location != destination &&
          !location.startsWith('/delivery/${runtime.pendingDeliveryId}/')) {
        return destination;
      }
    }

    if (location == '/auth/otp' &&
        runtime.stage == MobileAuthStage.mobileNumber) {
      return '/auth/mobile';
    }

    if (location == '/auth/biometric' &&
        runtime.stage != MobileAuthStage.biometricOffer) {
      return runtime.isAuthenticated
          ? runtime.postAuthenticationDestination()
          : '/auth/mobile';
    }

    if (location.startsWith('/auth/') && runtime.isAuthenticated) {
      return runtime.postAuthenticationDestination();
    }

    if (location.endsWith('/message')) {
      final id = state.pathParameters['id'];
      if (id == null || !runtime.hasRevealedMessage(id)) {
        return id == null ? '/inbox' : '/delivery/$id/login';
      }
    }

    return null;
  },
  routes: [
    GoRoute(
      path: '/',
      builder: (_, _) => AnimatedBuilder(
        animation: runtime,
        builder: (_, _) => SplashScreen(
          isLoading: runtime.isBooting,
          errorMessage: runtime.bootFailure?.messageFor(runtime.isArabic),
          onRetry: runtime.bootFailure == null ? null : runtime.bootstrap,
        ),
      ),
    ),
    GoRoute(
      path: '/auth/mobile',
      builder: (context, __) => AnimatedBuilder(
        animation: runtime,
        builder: (_, _) => MobileNumberScreen(
          state: runtime.mobileNumberState,
          onRequestOtp: (mobile) async {
            final success = await runtime.requestOtp(mobile);
            if (success && context.mounted) context.go('/auth/otp');
          },
        ),
      ),
    ),
    GoRoute(
      path: '/auth/otp',
      builder: (context, __) => AnimatedBuilder(
        animation: runtime,
        builder: (_, _) => OtpScreen(
          state: runtime.otpState,
          onVerify: (otp) async {
            final success = await runtime.verifyOtp(otp);
            if (success && context.mounted) context.go('/auth/biometric');
          },
          onResend: () async {
            await runtime.resendOtp();
          },
        ),
      ),
    ),
    GoRoute(
      path: '/auth/biometric',
      builder: (context, __) => AnimatedBuilder(
        animation: runtime,
        builder: (_, _) => BiometricScreen(
          state: runtime.biometricState,
          onEnable: () async {
            final success = await runtime.enableBiometrics();
            if (success && context.mounted) {
              context.go(runtime.postAuthenticationDestination());
            }
          },
          onSkip: () async {
            await runtime.skipBiometrics();
            if (context.mounted) {
              context.go(runtime.postAuthenticationDestination());
            }
          },
        ),
      ),
    ),
    GoRoute(
      path: '/inbox',
      builder: (context, __) => _InboxRuntimeRoute(
        runtime: runtime,
        onOpenDelivery: (deliveryId) {
          context.go('/delivery/$deliveryId/login');
        },
        onLoggedOut: () {
          if (context.mounted) context.go('/auth/mobile');
        },
      ),
    ),
    GoRoute(
      path: '/delivery/:id/login',
      builder: (context, state) => _SecureLoginRuntimeRoute(
        runtime: runtime,
        deliveryId: state.pathParameters['id']!,
        onRevealed: () {
          final id = state.pathParameters['id']!;
          if (context.mounted) context.go('/delivery/$id/message');
        },
      ),
    ),
    GoRoute(
      path: '/delivery/:id/message',
      builder: (_, state) => AnimatedBuilder(
        animation: runtime,
        builder: (_, _) => SecureMessageScreen(
          deliveryId: state.pathParameters['id']!,
          state: runtime.secureMessageState(state.pathParameters['id']!),
        ),
      ),
    ),
  ],
);

class _InboxRuntimeRoute extends StatefulWidget {
  const _InboxRuntimeRoute({
    required this.runtime,
    required this.onOpenDelivery,
    required this.onLoggedOut,
  });

  final AppRuntime runtime;
  final OpenDeliveryCallback onOpenDelivery;
  final VoidCallback onLoggedOut;

  @override
  State<_InboxRuntimeRoute> createState() => _InboxRuntimeRouteState();
}

class _InboxRuntimeRouteState extends State<_InboxRuntimeRoute> {
  @override
  void initState() {
    super.initState();
    if (widget.runtime.inboxState.phase == UiPhase.idle ||
        widget.runtime.inboxState.phase == UiPhase.empty) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) widget.runtime.refreshInbox();
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: widget.runtime,
      builder: (_, _) => InboxScreen(
        state: widget.runtime.inboxState,
        onRetry: widget.runtime.refreshInbox,
        onRefresh: widget.runtime.refreshInbox,
        onOpenDelivery: widget.onOpenDelivery,
        onLogout: () async {
          await widget.runtime.logout();
          widget.onLoggedOut();
        },
      ),
    );
  }
}

class _SecureLoginRuntimeRoute extends StatefulWidget {
  const _SecureLoginRuntimeRoute({
    required this.runtime,
    required this.deliveryId,
    required this.onRevealed,
  });

  final AppRuntime runtime;
  final String deliveryId;
  final VoidCallback onRevealed;

  @override
  State<_SecureLoginRuntimeRoute> createState() =>
      _SecureLoginRuntimeRouteState();
}

class _SecureLoginRuntimeRouteState extends State<_SecureLoginRuntimeRoute> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) widget.runtime.loadDelivery(widget.deliveryId);
    });
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: widget.runtime,
      builder: (_, _) => SecureLoginScreen(
        deliveryId: widget.deliveryId,
        state: widget.runtime.secureLoginState(widget.deliveryId),
        onAuthenticate: (deliveryId, username, password) async {
          final success = await widget.runtime.authenticateAndReveal(
            deliveryId: deliveryId,
            username: username,
            password: password,
          );
          if (success && mounted) widget.onRevealed();
        },
      ),
    );
  }
}
