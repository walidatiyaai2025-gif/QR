import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:da_secure/config/app_config.dart';
import 'package:da_secure/networking/mobile_device_registration_api.dart';
import 'package:da_secure/routing/app_navigation_state.dart';
import 'package:da_secure/security/secure_storage_service.dart';
import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';

class SafePushPayload {
  const SafePushPayload({
    required this.deliveryId,
    required this.category,
    required this.version,
  });

  final int deliveryId;
  final String category;
  final String version;

  static const _allowedCategories = <String>{
    'secure_delivery',
    'delivery',
    'reminder',
  };

  static const _forbiddenKeys = <String>{
    'body',
    'content',
    'securebody',
    'otp',
    'username',
    'password',
    'accesstoken',
    'refreshtoken',
    'revealtoken',
    'qrtoken',
    'sharetoken',
    'attachment',
    'attachments',
  };

  static SafePushPayload? tryParse(Map<String, dynamic> data) {
    for (final key in data.keys) {
      final normalized = key.toLowerCase().replaceAll(RegExp(r'[^a-z0-9]'), '');
      if (_forbiddenKeys.contains(normalized)) {
        return null;
      }
    }

    final rawDeliveryId = data['deliveryId'];
    final deliveryId = rawDeliveryId is int
        ? rawDeliveryId
        : int.tryParse(rawDeliveryId?.toString().trim() ?? '');
    if (deliveryId == null || deliveryId <= 0) {
      return null;
    }

    final version = (data['version']?.toString().trim().isNotEmpty ?? false)
        ? data['version'].toString().trim()
        : '1';
    if (version != '1') {
      return null;
    }

    final category = (data['notificationCategory'] ?? data['category'])
            ?.toString()
            .trim() ??
        'secure_delivery';
    if (!_allowedCategories.contains(category)) {
      return null;
    }

    return SafePushPayload(
      deliveryId: deliveryId,
      category: category,
      version: version,
    );
  }
}

abstract interface class PushMessagingPort {
  Future<void> requestPermission();
  Future<String?> getToken();
  Stream<String> get tokenRefresh;
  Stream<Map<String, dynamic>> get foregroundMessages;
  Stream<Map<String, dynamic>> get openedMessages;
  Future<Map<String, dynamic>?> getInitialMessage();
}

class FlutterFireMessagingPort implements PushMessagingPort {
  FlutterFireMessagingPort([FirebaseMessaging? messaging])
      : messaging = messaging ?? FirebaseMessaging.instance;

  final FirebaseMessaging messaging;

  @override
  Future<void> requestPermission() async {
    await messaging.requestPermission(
      alert: true,
      badge: true,
      sound: true,
      provisional: false,
    );
  }

  @override
  Future<String?> getToken() => messaging.getToken();

  @override
  Stream<String> get tokenRefresh => messaging.onTokenRefresh;

  @override
  Stream<Map<String, dynamic>> get foregroundMessages =>
      FirebaseMessaging.onMessage.map((message) => message.data);

  @override
  Stream<Map<String, dynamic>> get openedMessages =>
      FirebaseMessaging.onMessageOpenedApp.map((message) => message.data);

  @override
  Future<Map<String, dynamic>?> getInitialMessage() async {
    final message = await messaging.getInitialMessage();
    return message?.data;
  }
}

class FcmDeviceRegistrar {
  FcmDeviceRegistrar({
    required this.storage,
    required this.gateway,
    Random? random,
  }) : random = random ?? Random.secure();

  final MobileSessionStorage storage;
  final MobileDeviceRegistrationGateway gateway;
  final Random random;

  Future<bool> syncToken(String? rawToken) async {
    final token = rawToken?.trim() ?? '';
    if (token.isEmpty) {
      return false;
    }

    final accessToken = (await storage.readAccessToken())?.trim() ?? '';
    if (accessToken.isEmpty) {
      await storage.writePendingFcmToken(token);
      return false;
    }

    final deviceId = await _ensureDeviceId();
    final registered = await gateway.register(
      accessToken: accessToken,
      deviceId: deviceId,
      fcmToken: token,
      platform: Platform.isAndroid ? 'android' : 'unknown',
      appVersion: AppConfig.appVersion,
    );

    if (registered) {
      await storage.clearPendingFcmToken();
      return true;
    }

    await storage.writePendingFcmToken(token);
    return false;
  }

  Future<bool> syncPendingIfAuthenticated() async {
    final pending = await storage.readPendingFcmToken();
    if (pending == null || pending.trim().isEmpty) {
      return false;
    }
    return syncToken(pending);
  }

  Future<String> _ensureDeviceId() async {
    final existing = (await storage.readDeviceId())?.trim();
    if (existing != null && existing.isNotEmpty) {
      return existing;
    }

    final bytes = List<int>.generate(24, (_) => random.nextInt(256));
    final generated = base64UrlEncode(bytes).replaceAll('=', '');
    await storage.writeDeviceId(generated);
    return generated;
  }
}

class PushNavigationCoordinator {
  const PushNavigationCoordinator({
    required this.navigationState,
    required this.navigate,
  });

  final AppNavigationState navigationState;
  final void Function(String location) navigate;

  String? handle(Map<String, dynamic> data) {
    final payload = SafePushPayload.tryParse(data);
    if (payload == null) {
      return null;
    }

    final destination = navigationState.destinationForPush(
      payload.deliveryId.toString(),
    );
    navigate(destination);
    return destination;
  }
}

class FcmMessagingCoordinator {
  FcmMessagingCoordinator({
    required this.messaging,
    required this.registrar,
    required this.navigation,
    this.onForegroundPayload,
  });

  final PushMessagingPort messaging;
  final FcmDeviceRegistrar registrar;
  final PushNavigationCoordinator navigation;
  final void Function(SafePushPayload payload)? onForegroundPayload;

  StreamSubscription<String>? _tokenRefreshSubscription;
  StreamSubscription<Map<String, dynamic>>? _foregroundSubscription;
  StreamSubscription<Map<String, dynamic>>? _openedSubscription;
  bool _started = false;

  Future<void> start() async {
    if (_started) return;
    _started = true;

    await messaging.requestPermission();
    await registrar.syncToken(await messaging.getToken());

    _tokenRefreshSubscription = messaging.tokenRefresh.listen(
      (token) => unawaited(registrar.syncToken(token)),
    );
    _foregroundSubscription = messaging.foregroundMessages.listen((data) {
      final payload = SafePushPayload.tryParse(data);
      if (payload != null) {
        onForegroundPayload?.call(payload);
      }
    });
    _openedSubscription = messaging.openedMessages.listen(navigation.handle);

    final initialMessage = await messaging.getInitialMessage();
    if (initialMessage != null) {
      navigation.handle(initialMessage);
    }
  }

  Future<void> dispose() async {
    await _tokenRefreshSubscription?.cancel();
    await _foregroundSubscription?.cancel();
    await _openedSubscription?.cancel();
    _started = false;
  }
}

@pragma('vm:entry-point')
Future<void> firebaseMessagingBackgroundHandler(RemoteMessage message) async {
  await Firebase.initializeApp();
  // Deliberately do not render, route, persist, or log protected message data
  // from a background callback. Tap routing is handled by initial/opened events.
  SafePushPayload.tryParse(message.data);
}
