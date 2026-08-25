import 'dart:async';

import 'package:da_secure/security/secure_storage_service.dart';
import 'package:firebase_messaging/firebase_messaging.dart';

typedef DeliveryPushCallback = Future<void> Function(String deliveryId);
typedef ForegroundDeliveryCallback = Future<void> Function();
typedef RegisterDeviceCallback = Future<void> Function({
  required String fcmToken,
  required bool pushEnabled,
});

enum MobilePushAuthorization { denied, authorized, provisional }

abstract interface class MobileMessagingPort {
  Stream<Map<String, dynamic>> get openedMessages;
  Stream<Map<String, dynamic>> get foregroundMessages;
  Stream<String> get tokenRefresh;

  Future<Map<String, dynamic>?> getInitialMessageData();
  Future<MobilePushAuthorization> requestPermission();
  Future<MobilePushAuthorization> getAuthorizationStatus();
  Future<String?> getToken();
}

class FlutterFireMessagingPort implements MobileMessagingPort {
  FlutterFireMessagingPort(this.messaging);

  final FirebaseMessaging messaging;

  @override
  Stream<Map<String, dynamic>> get openedMessages =>
      FirebaseMessaging.onMessageOpenedApp.map((message) => message.data);

  @override
  Stream<Map<String, dynamic>> get foregroundMessages =>
      FirebaseMessaging.onMessage.map((message) => message.data);

  @override
  Stream<String> get tokenRefresh => messaging.onTokenRefresh;

  @override
  Future<Map<String, dynamic>?> getInitialMessageData() async =>
      (await messaging.getInitialMessage())?.data;

  @override
  Future<MobilePushAuthorization> requestPermission() async =>
      _mapAuthorization(
        (await messaging.requestPermission(
          alert: true,
          badge: true,
          sound: true,
        )).authorizationStatus,
      );

  @override
  Future<MobilePushAuthorization> getAuthorizationStatus() async =>
      _mapAuthorization(
        (await messaging.getNotificationSettings()).authorizationStatus,
      );

  @override
  Future<String?> getToken() => messaging.getToken();

  static MobilePushAuthorization _mapAuthorization(
    AuthorizationStatus status,
  ) => switch (status) {
    AuthorizationStatus.authorized => MobilePushAuthorization.authorized,
    AuthorizationStatus.provisional => MobilePushAuthorization.provisional,
    _ => MobilePushAuthorization.denied,
  };
}

class FirebaseMessagingCoordinator {
  FirebaseMessagingCoordinator({
    required this.messaging,
    required this.registerDevice,
    required this.storage,
    required this.isAuthenticated,
    required this.onDeliveryOpened,
    required this.onForegroundDelivery,
  });

  final MobileMessagingPort messaging;
  final RegisterDeviceCallback registerDevice;
  final SecureStorageService storage;
  final bool Function() isAuthenticated;
  final DeliveryPushCallback onDeliveryOpened;
  final ForegroundDeliveryCallback onForegroundDelivery;

  StreamSubscription<Map<String, dynamic>>? _openedSubscription;
  StreamSubscription<Map<String, dynamic>>? _foregroundSubscription;
  StreamSubscription<String>? _tokenSubscription;
  bool _started = false;

  Future<void> start() async {
    if (_started) return;
    _started = true;

    _openedSubscription = messaging.openedMessages.listen(_handleOpenedData);
    _foregroundSubscription = messaging.foregroundMessages.listen(
      _handleForegroundData,
    );
    _tokenSubscription = messaging.tokenRefresh.listen((token) async {
      if (!isAuthenticated()) return;
      try {
        await _registerToken(token, requestPermission: false);
      } on Object {
        // Token rotation remains non-fatal; the next rotation/auth registration retries.
      }
    });

    final initial = await messaging.getInitialMessageData();
    if (initial != null) {
      await _handleOpenedData(initial);
    }
  }

  Future<bool> registerAuthenticatedDevice({
    bool requestPermissionIfNeeded = true,
  }) async {
    if (!isAuthenticated()) return false;

    MobilePushAuthorization authorization;
    final prompted = await storage.notificationPermissionPrompted();
    if (requestPermissionIfNeeded && !prompted) {
      authorization = await messaging.requestPermission();
      await storage.markNotificationPermissionPrompted();
    } else {
      authorization = await messaging.getAuthorizationStatus();
    }

    final token = await messaging.getToken();
    if (token == null || token.trim().isEmpty) return false;

    await registerDevice(
      fcmToken: token.trim(),
      pushEnabled: _pushEnabled(authorization),
    );
    return true;
  }

  Future<void> _registerToken(
    String token, {
    required bool requestPermission,
  }) async {
    if (!isAuthenticated() || token.trim().isEmpty) return;
    final authorization = requestPermission
        ? await messaging.requestPermission()
        : await messaging.getAuthorizationStatus();
    await registerDevice(
      fcmToken: token.trim(),
      pushEnabled: _pushEnabled(authorization),
    );
  }

  Future<void> _handleOpenedData(Map<String, dynamic> data) async {
    final deliveryId = validatedSecureDeliveryId(data);
    if (deliveryId == null) return;
    await onDeliveryOpened(deliveryId);
  }

  Future<void> _handleForegroundData(Map<String, dynamic> data) async {
    if (validatedSecureDeliveryId(data) == null) return;
    if (!isAuthenticated()) return;
    await onForegroundDelivery();
  }

  Future<void> dispose() async {
    await _openedSubscription?.cancel();
    await _foregroundSubscription?.cancel();
    await _tokenSubscription?.cancel();
  }
}

bool _pushEnabled(MobilePushAuthorization authorization) =>
    authorization == MobilePushAuthorization.authorized ||
    authorization == MobilePushAuthorization.provisional;

String? validatedSecureDeliveryId(Map<String, dynamic> data) {
  if (data['notificationCategory'] != 'secure_delivery') return null;
  if (data['version']?.toString() != '1') return null;
  final id = int.tryParse(data['deliveryId']?.toString() ?? '');
  if (id == null || id <= 0) return null;
  return id.toString();
}
