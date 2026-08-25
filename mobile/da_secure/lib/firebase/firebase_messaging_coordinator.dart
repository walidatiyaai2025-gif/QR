import 'dart:async';

import 'package:da_secure/repositories/device_repository.dart';
import 'package:da_secure/security/secure_storage_service.dart';
import 'package:firebase_messaging/firebase_messaging.dart';

typedef DeliveryPushCallback = Future<void> Function(String deliveryId);
typedef ForegroundDeliveryCallback = Future<void> Function();

class FirebaseMessagingCoordinator {
  FirebaseMessagingCoordinator({
    required this.messaging,
    required this.devices,
    required this.storage,
    required this.isAuthenticated,
    required this.onDeliveryOpened,
    required this.onForegroundDelivery,
  });

  final FirebaseMessaging messaging;
  final DeviceRepository devices;
  final SecureStorageService storage;
  final bool Function() isAuthenticated;
  final DeliveryPushCallback onDeliveryOpened;
  final ForegroundDeliveryCallback onForegroundDelivery;

  StreamSubscription<RemoteMessage>? _openedSubscription;
  StreamSubscription<RemoteMessage>? _foregroundSubscription;
  StreamSubscription<String>? _tokenSubscription;
  bool _started = false;

  Future<void> start() async {
    if (_started) return;
    _started = true;

    _openedSubscription =
        FirebaseMessaging.onMessageOpenedApp.listen(_handleOpenedMessage);
    _foregroundSubscription =
        FirebaseMessaging.onMessage.listen(_handleForegroundMessage);
    _tokenSubscription = messaging.onTokenRefresh.listen((token) async {
      if (!isAuthenticated()) return;
      try {
        await _registerToken(token, requestPermission: false);
      } on Object {
        // Token rotation remains non-fatal; the next rotation/auth registration retries.
      }
    });

    final initial = await messaging.getInitialMessage();
    if (initial != null) {
      await _handleOpenedMessage(initial);
    }
  }

  Future<bool> registerAuthenticatedDevice({
    bool requestPermissionIfNeeded = true,
  }) async {
    if (!isAuthenticated()) return false;

    NotificationSettings settings;
    final prompted = await storage.notificationPermissionPrompted();
    if (requestPermissionIfNeeded && !prompted) {
      settings = await messaging.requestPermission(
        alert: true,
        badge: true,
        sound: true,
      );
      await storage.markNotificationPermissionPrompted();
    } else {
      settings = await messaging.getNotificationSettings();
    }

    final token = await messaging.getToken();
    if (token == null || token.trim().isEmpty) return false;

    final pushEnabled =
        settings.authorizationStatus == AuthorizationStatus.authorized ||
            settings.authorizationStatus == AuthorizationStatus.provisional;
    await devices.register(fcmToken: token, pushEnabled: pushEnabled);
    return true;
  }

  Future<void> _registerToken(
    String token, {
    required bool requestPermission,
  }) async {
    if (!isAuthenticated() || token.trim().isEmpty) return;
    final settings = requestPermission
        ? await messaging.requestPermission(alert: true, badge: true, sound: true)
        : await messaging.getNotificationSettings();
    final pushEnabled =
        settings.authorizationStatus == AuthorizationStatus.authorized ||
            settings.authorizationStatus == AuthorizationStatus.provisional;
    await devices.register(fcmToken: token, pushEnabled: pushEnabled);
  }

  Future<void> _handleOpenedMessage(RemoteMessage message) async {
    final deliveryId = _validatedDeliveryId(message.data);
    if (deliveryId == null) return;
    await onDeliveryOpened(deliveryId);
  }

  Future<void> _handleForegroundMessage(RemoteMessage message) async {
    if (_validatedDeliveryId(message.data) == null) return;
    if (!isAuthenticated()) return;
    await onForegroundDelivery();
  }

  String? _validatedDeliveryId(Map<String, dynamic> data) {
    if (data['notificationCategory'] != 'secure_delivery') return null;
    if (data['version']?.toString() != '1') return null;
    final id = int.tryParse(data['deliveryId']?.toString() ?? '');
    if (id == null || id <= 0) return null;
    return id.toString();
  }

  Future<void> dispose() async {
    await _openedSubscription?.cancel();
    await _foregroundSubscription?.cancel();
    await _tokenSubscription?.cancel();
  }
}
