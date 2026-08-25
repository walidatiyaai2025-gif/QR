import 'package:da_secure/config/app_config.dart';
import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/firebase/firebase_bootstrap.dart';
import 'package:da_secure/firebase/firebase_messaging_coordinator.dart';
import 'package:da_secure/networking/api_client.dart';
import 'package:da_secure/repositories/auth_repository.dart';
import 'package:da_secure/repositories/device_repository.dart';
import 'package:da_secure/repositories/inbox_repository.dart';
import 'package:da_secure/routing/app_router.dart';
import 'package:da_secure/runtime/app_runtime.dart';
import 'package:da_secure/security/secure_storage_service.dart';
import 'package:da_secure/services/biometric_service.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:go_router/go_router.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await FirebaseBootstrap.initializeClient();

  const secureStorage = FlutterSecureStorage();
  final storage = SecureStorageService(secureStorage);
  final apiClient = ApiClient(storage: storage);
  final auth = AuthRepository(client: apiClient, storage: storage);
  final inbox = InboxRepository(apiClient);
  final devices = DeviceRepository(client: apiClient, storage: storage);
  final runtime = AppRuntime(
    auth: auth,
    inbox: inbox,
    storage: storage,
    biometrics: BiometricService(),
    client: apiClient,
  );
  final messaging = FirebaseMessagingCoordinator(
    messaging: FlutterFireMessagingPort(FirebaseMessaging.instance),
    registerDevice: ({required fcmToken, required pushEnabled}) async {
      await devices.register(fcmToken: fcmToken, pushEnabled: pushEnabled);
    },
    storage: storage,
    isAuthenticated: () => runtime.isAuthenticated,
    onDeliveryOpened: runtime.handlePushOpened,
    onForegroundDelivery: runtime.refreshInbox,
  );
  runtime.attachMessaging(messaging);

  runApp(DaSecureApp(runtime: runtime));
}

class DaSecureApp extends StatefulWidget {
  const DaSecureApp({required this.runtime, super.key});

  final AppRuntime runtime;

  @override
  State<DaSecureApp> createState() => _DaSecureAppState();
}

class _DaSecureAppState extends State<DaSecureApp> {
  late final GoRouter _router;

  @override
  void initState() {
    super.initState();
    _router = createAppRouter(widget.runtime);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      widget.runtime.bootstrap();
    });
  }

  @override
  void dispose() {
    _router.dispose();
    widget.runtime.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp.router(
      debugShowCheckedModeBanner: false,
      title: AppConfig.appName,
      theme: DaSecureTheme.light,
      supportedLocales: const [Locale('ar'), Locale('en')],
      localizationsDelegates: GlobalMaterialLocalizations.delegates,
      localeResolutionCallback: (deviceLocale, supportedLocales) {
        if (deviceLocale?.languageCode == 'en') {
          return const Locale('en');
        }
        return const Locale('ar');
      },
      routerConfig: _router,
    );
  }
}
