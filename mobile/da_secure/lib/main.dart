import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/firebase/firebase_bootstrap.dart';
import 'package:da_secure/firebase/firebase_messaging_service.dart';
import 'package:da_secure/localization/da_strings.dart';
import 'package:da_secure/networking/api_client.dart';
import 'package:da_secure/networking/mobile_device_registration_client.dart';
import 'package:da_secure/routing/app_router.dart';
import 'package:da_secure/security/secure_storage_service.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

FirebaseMessagingService? firebaseMessagingService;
late FirebaseBootstrapState firebaseBootstrapState;

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  firebaseBootstrapState = await FirebaseBootstrap.initialize();
  runApp(const DaSecureApp());

  if (firebaseBootstrapState.isReady) {
    WidgetsBinding.instance.addPostFrameCallback((_) async {
      final service = FirebaseMessagingService(
        messaging: FirebaseMessaging.instance,
        storage: const SecureStorageService(FlutterSecureStorage()),
        registrationClient: MobileDeviceRegistrationClient(ApiClient()),
      );
      firebaseMessagingService = service;
      mobilePushSessionBridge.attach(service);
      await service.initialize();
    });
  }
}

class DaSecureApp extends StatelessWidget {
  const DaSecureApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp.router(
      title: 'DA Secure',
      debugShowCheckedModeBanner: false,
      theme: DaSecureTheme.light,
      routerConfig: appRouter,
      locale: const Locale('ar'),
      supportedLocales: DaStrings.supportedLocales,
      localizationsDelegates: GlobalMaterialLocalizations.delegates,
    );
  }
}
