import 'package:da_secure/features/auth/presentation/biometric_screen.dart';
import 'package:da_secure/features/auth/presentation/mobile_number_screen.dart';
import 'package:da_secure/features/auth/presentation/otp_screen.dart';
import 'package:da_secure/features/inbox/presentation/inbox_screen.dart';
import 'package:da_secure/features/inbox/presentation/secure_login_screen.dart';
import 'package:da_secure/features/inbox/presentation/secure_message_screen.dart';
import 'package:da_secure/features/splash/presentation/splash_screen.dart';
import 'package:go_router/go_router.dart';

final appRouter = GoRouter(
  initialLocation: '/',
  routes: [
    GoRoute(path: '/', builder: (_, __) => const SplashScreen()),
    GoRoute(path: '/auth/mobile', builder: (_, __) => const MobileNumberScreen()),
    GoRoute(path: '/auth/otp', builder: (_, __) => const OtpScreen()),
    GoRoute(path: '/auth/biometric', builder: (_, __) => const BiometricScreen()),
    GoRoute(path: '/inbox', builder: (_, __) => const InboxScreen()),
    GoRoute(path: '/delivery/:id/login', builder: (_, state) => SecureLoginScreen(deliveryId: state.pathParameters['id']!)),
    GoRoute(path: '/delivery/:id/message', builder: (_, state) => SecureMessageScreen(deliveryId: state.pathParameters['id']!)),
  ],
);
