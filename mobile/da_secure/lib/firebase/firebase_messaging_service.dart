import 'dart:async';
import 'dart:io';

import 'package:da_secure/config/app_config.dart';
import 'package:da_secure/firebase/firebase_bootstrap.dart';
import 'package:da_secure/networking/mobile_device_registration_client.dart';
import 'package:da_secure/routing/app_navigation_state.dart';
import 'package:da_secure/routing/app_router.dart';
import 'package:da_secure/security/secure_storage_service.dart';
import 'package:firebase_messaging/firebase_messaging.dart';

class FirebaseMessagingService {
  FirebaseMessagingService({
    required FirebaseMessaging messaging,
    required SecureStorageService storage,
    required MobileDeviceRegistrationClient registrationClient,
  })  : _messaging = messaging,
        _storage = storage,
        _registrationClient = registrationClient;

  final FirebaseMessaging _messaging;
  final SecureStorageService _storage;
  final MobileDeviceRegistrationClient _registrationClient;
  final StreamController<PushRoutingPayload> _foreground =
      StreamController<PushRoutingPayload>.broadcast();

  StreamSubscription<RemoteMessage>? _foregroundSubscription;
  StreamSubscription<RemoteMessage>? _openedSubscription;
  StreamSubscription<String>? _tokenSubscription;
  String? _accessToken;
  bool _pushEnabled = false;

  Stream<PushRoutingPayload> get foregroundMessages => _foreground.stream;
  bool get pushEnabled => _pushEnabled;

  Future<void> initialize() async {
    final settings = await _messaging.requestPermission(
      alert: true,
      badge: true,
      sound: true,
      provisional: false,
    );
    _pushEnabled = settings.authorizationStatus == AuthorizationStatus.authorized ||
        settings.authorizationStatus == AuthorizationStatus.provisional;

    if (_pushEnabled) {
      final token = await _messaging.getToken();
      if (token != null && token.trim().isNotEmpty) {
        await _onToken(token);
      }
    }

    _tokenSubscription = _messaging.onTokenRefresh.listen(_onToken);
    _foregroundSubscription = FirebaseMessaging.onMessage.listen((message) {
      final payload = PushRoutingPayload.tryParse(message.data);
      if (payload != null) {
        _foreground.add(payload);
      }
    });
    _openedSubscription = FirebaseMessaging.onMessageOpenedApp.listen(_routeMessage);

    final initial = await _messaging.getInitialMessage();
    if (initial != null) {
      _routeMessage(initial);
    }
  }

  Future<void> onAuthenticated(String accessToken) async {
    final normalized = accessToken.trim();
    if (normalized.isEmpty) {
      return;
    }
    _accessToken = normalized;
    await _flushPendingToken();
  }

  void onSignedOut() {
    _accessToken = null;
  }

  Future<void> _onToken(String token) async {
    final normalized = token.trim();
    if (normalized.isEmpty) {
      return;
    }
    await _storage.savePendingFcmToken(normalized);
    await _flushPendingToken();
  }

  Future<void> _flushPendingToken() async {
    final accessToken = _accessToken;
    final token = await _storage.readPendingFcmToken();
    if (accessToken == null || token == null) {
      return;
    }
    final deviceId = await _storage.installationId();
    final registered = await _registrationClient.register(
      accessToken: accessToken,
      request: MobileDeviceRegistrationRequest(
        deviceId: deviceId,
        fcmToken: token,
        platform: Platform.isAndroid ? 'android' : Platform.operatingSystem,
        appVersion: AppConfig.appVersion,
        pushEnabled: _pushEnabled,
      ),
    );
    if (registered) {
      await _storage.clearPendingFcmToken();
    }
  }

  void _routeMessage(RemoteMessage message) {
    final payload = PushRoutingPayload.tryParse(message.data);
    if (payload == null) {
      return;
    }
    final destination = appNavigationState.destinationForPush(payload.deliveryId);
    appRouter.go(destination);
  }

  Future<void> dispose() async {
    await _foregroundSubscription?.cancel();
    await _openedSubscription?.cancel();
    await _tokenSubscription?.cancel();
    await _foreground.close();
  }
}

class MobilePushSessionBridge {
  FirebaseMessagingService? _service;

  void attach(FirebaseMessagingService service) {
    _service = service;
  }

  Future<void> onAuthenticated(String realAccessToken) async {
    await _service?.onAuthenticated(realAccessToken);
  }

  void onSignedOut() {
    _service?.onSignedOut();
  }
}

final mobilePushSessionBridge = MobilePushSessionBridge();
