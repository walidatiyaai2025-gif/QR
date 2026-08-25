import 'dart:async';

import 'package:da_secure/firebase/firebase_messaging_service.dart';
import 'package:da_secure/networking/api_client.dart';
import 'package:da_secure/networking/mobile_device_registration_api.dart';
import 'package:da_secure/routing/app_navigation_state.dart';
import 'package:da_secure/routing/app_router.dart';
import 'package:da_secure/security/secure_storage_service.dart';
import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

abstract final class FirebaseBootstrap {
  static FcmMessagingCoordinator? _coordinator;
  static FcmDeviceRegistrar? _registrar;
  static bool _navigationListenerInstalled = false;

  static Future<void> initializeClient() async {
    // Android google-services.json is present. Initialization failures must remain
    // visible to the runtime/QA path; never silently convert a broken Firebase
    // configuration into an apparently healthy application state.
    await Firebase.initializeApp();
    FirebaseMessaging.onBackgroundMessage(firebaseMessagingBackgroundHandler);
  }

  static Future<void> startMessaging() async {
    if (_coordinator != null) return;

    const storage = SecureStorageService(FlutterSecureStorage());
    final apiClient = ApiClient();
    final registrar = FcmDeviceRegistrar(
      storage: storage,
      gateway: MobileDeviceRegistrationApi(apiClient.dio),
    );
    final navigation = PushNavigationCoordinator(
      navigationState: appNavigationState,
      navigate: appRouter.go,
    );
    final coordinator = FcmMessagingCoordinator(
      messaging: FlutterFireMessagingPort(),
      registrar: registrar,
      navigation: navigation,
    );

    _registrar = registrar;
    _coordinator = coordinator;
    if (!_navigationListenerInstalled) {
      appNavigationState.addListener(_syncPendingTokenAfterAuthentication);
      _navigationListenerInstalled = true;
    }
    await coordinator.start();
  }

  static void _syncPendingTokenAfterAuthentication() {
    if (!appNavigationState.isAuthenticated) return;
    final registrar = _registrar;
    if (registrar != null) {
      unawaited(registrar.syncPendingIfAuthenticated());
    }
  }
}
