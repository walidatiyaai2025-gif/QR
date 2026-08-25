import 'dart:async';
import 'dart:math';

import 'package:da_secure/firebase/firebase_messaging_service.dart';
import 'package:da_secure/networking/mobile_device_registration_api.dart';
import 'package:da_secure/routing/app_navigation_state.dart';
import 'package:da_secure/security/secure_storage_service.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('FCM payload rejects missing or malformed delivery id', () {
    expect(SafePushPayload.tryParse(<String, dynamic>{}), isNull);
    expect(
      SafePushPayload.tryParse(<String, dynamic>{'deliveryId': 'not-a-number'}),
      isNull,
    );
    expect(
      SafePushPayload.tryParse(<String, dynamic>{'deliveryId': 0}),
      isNull,
    );
  });

  test('FCM payload rejects protected fields and unsupported versions', () {
    expect(
      SafePushPayload.tryParse(<String, dynamic>{
        'deliveryId': '42',
        'notificationCategory': 'secure_delivery',
        'version': '1',
        'password': 'must-not-travel',
      }),
      isNull,
    );
    expect(
      SafePushPayload.tryParse(<String, dynamic>{
        'deliveryId': '42',
        'notificationCategory': 'secure_delivery',
        'version': '2',
      }),
      isNull,
    );
  });

  test('unauthenticated notification tap preserves pending delivery', () {
    final state = AppNavigationState();
    final destinations = <String>[];
    final coordinator = PushNavigationCoordinator(
      navigationState: state,
      navigate: destinations.add,
    );

    final destination = coordinator.handle(<String, dynamic>{
      'deliveryId': '42',
      'notificationCategory': 'secure_delivery',
      'version': '1',
    });

    expect(destination, '/auth/mobile');
    expect(state.pendingDeliveryId, '42');
    expect(destinations, <String>['/auth/mobile']);
  });

  test('authenticated notification tap routes to secure login', () {
    final state = AppNavigationState()..completeAuthentication();
    final destinations = <String>[];
    final coordinator = PushNavigationCoordinator(
      navigationState: state,
      navigate: destinations.add,
    );

    final destination = coordinator.handle(<String, dynamic>{
      'deliveryId': 42,
      'category': 'reminder',
      'version': '1',
    });

    expect(destination, '/delivery/42/login');
    expect(destinations, <String>['/delivery/42/login']);
  });

  test('FCM token waits for authenticated session then registers', () async {
    final storage = FakeStorage();
    final gateway = FakeGateway();
    final registrar = FcmDeviceRegistrar(
      storage: storage,
      gateway: gateway,
      random: Random(7),
    );

    expect(await registrar.syncToken('fcm-token-1'), isFalse);
    expect(storage.pendingFcmToken, 'fcm-token-1');
    expect(gateway.calls, isEmpty);

    storage.accessToken = 'real-server-issued-access-token';
    expect(await registrar.syncPendingIfAuthenticated(), isTrue);
    expect(gateway.calls, hasLength(1));
    expect(gateway.calls.single.fcmToken, 'fcm-token-1');
    expect(gateway.calls.single.accessToken, storage.accessToken);
    expect(gateway.calls.single.deviceId, isNotEmpty);
    expect(storage.pendingFcmToken, isNull);
  });

  test('foreground handler exposes safe routing payload only', () async {
    final messaging = FakeMessagingPort();
    final storage = FakeStorage()..accessToken = 'access-token';
    final gateway = FakeGateway();
    final registrar = FcmDeviceRegistrar(
      storage: storage,
      gateway: gateway,
      random: Random(9),
    );
    final foreground = <SafePushPayload>[];
    final coordinator = FcmMessagingCoordinator(
      messaging: messaging,
      registrar: registrar,
      navigation: PushNavigationCoordinator(
        navigationState: AppNavigationState(),
        navigate: (_) {},
      ),
      onForegroundPayload: foreground.add,
    );

    await coordinator.start();
    messaging.foreground.add(<String, dynamic>{
      'deliveryId': '7',
      'notificationCategory': 'secure_delivery',
      'version': '1',
    });
    messaging.foreground.add(<String, dynamic>{
      'deliveryId': '8',
      'notificationCategory': 'secure_delivery',
      'version': '1',
      'secureBody': 'TOP SECRET',
    });
    await Future<void>.delayed(Duration.zero);

    expect(foreground, hasLength(1));
    expect(foreground.single.deliveryId, 7);
    await coordinator.dispose();
    await messaging.dispose();
  });

  test(
    'terminated-state initial message resumes through auth boundary',
    () async {
      final messaging = FakeMessagingPort()
        ..initialMessage = <String, dynamic>{
          'deliveryId': '99',
          'category': 'delivery',
          'version': '1',
        };
      final state = AppNavigationState();
      final destinations = <String>[];
      final coordinator = FcmMessagingCoordinator(
        messaging: messaging,
        registrar: FcmDeviceRegistrar(
          storage: FakeStorage(),
          gateway: FakeGateway(),
          random: Random(11),
        ),
        navigation: PushNavigationCoordinator(
          navigationState: state,
          navigate: destinations.add,
        ),
      );

      await coordinator.start();

      expect(destinations, <String>['/auth/mobile']);
      expect(state.pendingDeliveryId, '99');
      await coordinator.dispose();
      await messaging.dispose();
    },
  );
}

class RegistrationCall {
  const RegistrationCall({
    required this.accessToken,
    required this.deviceId,
    required this.fcmToken,
  });

  final String accessToken;
  final String deviceId;
  final String fcmToken;
}

class FakeGateway implements MobileDeviceRegistrationGateway {
  final List<RegistrationCall> calls = <RegistrationCall>[];
  bool accepted = true;

  @override
  Future<bool> register({
    required String accessToken,
    required String deviceId,
    required String fcmToken,
    required String platform,
    required String appVersion,
  }) async {
    calls.add(
      RegistrationCall(
        accessToken: accessToken,
        deviceId: deviceId,
        fcmToken: fcmToken,
      ),
    );
    return accepted;
  }
}

class FakeStorage implements MobileSessionStorage {
  String? accessToken;
  String? refreshToken;
  String? deviceId;
  String? pendingFcmToken;

  @override
  Future<void> clearPendingFcmToken() async => pendingFcmToken = null;

  @override
  Future<void> clearSession() async {
    accessToken = null;
    refreshToken = null;
  }

  @override
  Future<String?> readAccessToken() async => accessToken;

  @override
  Future<String?> readDeviceId() async => deviceId;

  @override
  Future<String?> readPendingFcmToken() async => pendingFcmToken;

  @override
  Future<String?> readRefreshToken() async => refreshToken;

  @override
  Future<void> writeDeviceId(String value) async => deviceId = value;

  @override
  Future<void> writePendingFcmToken(String token) async =>
      pendingFcmToken = token;

  @override
  Future<void> writeSession({
    required String accessToken,
    required String refreshToken,
  }) async {
    this.accessToken = accessToken;
    this.refreshToken = refreshToken;
  }
}

class FakeMessagingPort implements PushMessagingPort {
  final StreamController<String> tokenRefreshController =
      StreamController<String>.broadcast();
  final StreamController<Map<String, dynamic>> foreground =
      StreamController<Map<String, dynamic>>.broadcast();
  final StreamController<Map<String, dynamic>> opened =
      StreamController<Map<String, dynamic>>.broadcast();

  String? token;
  Map<String, dynamic>? initialMessage;
  bool permissionRequested = false;

  @override
  Stream<Map<String, dynamic>> get foregroundMessages => foreground.stream;

  @override
  Future<Map<String, dynamic>?> getInitialMessage() async => initialMessage;

  @override
  Future<String?> getToken() async => token;

  @override
  Stream<Map<String, dynamic>> get openedMessages => opened.stream;

  @override
  Future<void> requestPermission() async => permissionRequested = true;

  @override
  Stream<String> get tokenRefresh => tokenRefreshController.stream;

  Future<void> dispose() async {
    await tokenRefreshController.close();
    await foreground.close();
    await opened.close();
  }
}
