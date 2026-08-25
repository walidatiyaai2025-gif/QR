import 'dart:async';

import 'package:da_secure/firebase/firebase_messaging_coordinator.dart';
import 'package:da_secure/security/secure_storage_service.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  setUp(() {
    FlutterSecureStorage.setMockInitialValues(<String, String>{});
  });

  test('authenticated session registers current FCM token', () async {
    final harness = _Harness(authenticated: true);
    harness.port.authorization = MobilePushAuthorization.authorized;
    harness.port.token = 'fcm-token-1';

    final registered = await harness.coordinator.registerAuthenticatedDevice();

    expect(registered, isTrue);
    expect(harness.registrations, [
      {'token': 'fcm-token-1', 'pushEnabled': true},
    ]);
    expect(harness.port.permissionRequests, 1);
    await harness.dispose();
  });

  test('notification permission denial keeps application registration truthful', () async {
    final harness = _Harness(authenticated: true);
    harness.port.authorization = MobilePushAuthorization.denied;
    harness.port.token = 'fcm-token-1';

    final registered = await harness.coordinator.registerAuthenticatedDevice();

    expect(registered, isTrue);
    expect(harness.registrations.single['pushEnabled'], isFalse);
    await harness.dispose();
  });

  test('notification permission is not repeatedly requested', () async {
    final harness = _Harness(authenticated: true);
    harness.port.authorization = MobilePushAuthorization.authorized;
    harness.port.token = 'fcm-token-1';

    await harness.coordinator.registerAuthenticatedDevice();
    await harness.coordinator.registerAuthenticatedDevice();

    expect(harness.port.permissionRequests, 1);
    expect(harness.port.settingsReads, 1);
    expect(harness.registrations, hasLength(2));
    await harness.dispose();
  });

  test('unauthenticated session never fakes device registration', () async {
    final harness = _Harness(authenticated: false);
    harness.port.token = 'fcm-token-1';

    final registered = await harness.coordinator.registerAuthenticatedDevice();

    expect(registered, isFalse);
    expect(harness.registrations, isEmpty);
    expect(harness.port.permissionRequests, 0);
    expect(harness.port.tokenReads, 0);
    await harness.dispose();
  });

  test('empty Firebase token does not register a device', () async {
    final harness = _Harness(authenticated: true);
    harness.port.token = '  ';

    final registered = await harness.coordinator.registerAuthenticatedDevice();

    expect(registered, isFalse);
    expect(harness.registrations, isEmpty);
    await harness.dispose();
  });

  test('authenticated FCM token refresh updates server registration', () async {
    final harness = _Harness(authenticated: true);
    harness.port.authorization = MobilePushAuthorization.authorized;
    await harness.coordinator.start();

    harness.port.tokenRefreshController.add('rotated-token');
    await _flushEvents();

    expect(harness.registrations, [
      {'token': 'rotated-token', 'pushEnabled': true},
    ]);
    await harness.dispose();
  });

  test('unauthenticated FCM token refresh is ignored', () async {
    final harness = _Harness(authenticated: false);
    await harness.coordinator.start();

    harness.port.tokenRefreshController.add('rotated-token');
    await _flushEvents();

    expect(harness.registrations, isEmpty);
    await harness.dispose();
  });

  test('authenticated notification tap routes only the delivery id', () async {
    final harness = _Harness(authenticated: true);
    await harness.coordinator.start();

    harness.port.openedController.add(<String, dynamic>{
      'notificationCategory': 'secure_delivery',
      'version': '1',
      'deliveryId': '42',
      'protectedBody': 'must-not-be-consumed',
      'password': 'must-not-be-consumed',
    });
    await _flushEvents();

    expect(harness.openedDeliveries, ['42']);
    await harness.dispose();
  });

  test('terminated notification launch resumes through the same delivery callback', () async {
    final harness = _Harness(authenticated: false);
    harness.port.initialData = <String, dynamic>{
      'notificationCategory': 'secure_delivery',
      'version': 1,
      'deliveryId': 77,
    };

    await harness.coordinator.start();

    expect(harness.openedDeliveries, ['77']);
    await harness.dispose();
  });

  test('missing or invalid delivery id is ignored safely', () async {
    final harness = _Harness(authenticated: true);
    await harness.coordinator.start();

    harness.port.openedController.add(<String, dynamic>{
      'notificationCategory': 'secure_delivery',
      'version': '1',
    });
    harness.port.openedController.add(<String, dynamic>{
      'notificationCategory': 'secure_delivery',
      'version': '1',
      'deliveryId': '-5',
    });
    harness.port.openedController.add(<String, dynamic>{
      'notificationCategory': 'secure_delivery',
      'version': '1',
      'deliveryId': 'bad',
    });
    await _flushEvents();

    expect(harness.openedDeliveries, isEmpty);
    await harness.dispose();
  });

  test('wrong notification category or version is ignored', () async {
    final harness = _Harness(authenticated: true);
    await harness.coordinator.start();

    harness.port.openedController.add(<String, dynamic>{
      'notificationCategory': 'other',
      'version': '1',
      'deliveryId': '42',
    });
    harness.port.openedController.add(<String, dynamic>{
      'notificationCategory': 'secure_delivery',
      'version': '2',
      'deliveryId': '42',
    });
    await _flushEvents();

    expect(harness.openedDeliveries, isEmpty);
    await harness.dispose();
  });

  test('authenticated foreground push refreshes inbox without revealing content', () async {
    final harness = _Harness(authenticated: true);
    await harness.coordinator.start();

    harness.port.foregroundController.add(<String, dynamic>{
      'notificationCategory': 'secure_delivery',
      'version': '1',
      'deliveryId': '42',
      'contentArabicHtml': '<p>must stay ignored</p>',
      'attachment': 'must stay ignored',
    });
    await _flushEvents();

    expect(harness.foregroundRefreshes, 1);
    expect(harness.openedDeliveries, isEmpty);
    await harness.dispose();
  });

  test('unauthenticated foreground push cannot refresh protected inbox', () async {
    final harness = _Harness(authenticated: false);
    await harness.coordinator.start();

    harness.port.foregroundController.add(<String, dynamic>{
      'notificationCategory': 'secure_delivery',
      'version': '1',
      'deliveryId': '42',
    });
    await _flushEvents();

    expect(harness.foregroundRefreshes, 0);
    await harness.dispose();
  });

  test('payload validator accepts only positive secure-delivery ids', () {
    expect(
      validatedSecureDeliveryId(<String, dynamic>{
        'notificationCategory': 'secure_delivery',
        'version': 1,
        'deliveryId': 42,
      }),
      '42',
    );
    expect(
      validatedSecureDeliveryId(<String, dynamic>{
        'notificationCategory': 'secure_delivery',
        'version': 1,
        'deliveryId': 0,
      }),
      isNull,
    );
  });
}

class _Harness {
  _Harness({required bool authenticated}) : _authenticated = authenticated {
    FlutterSecureStorage.setMockInitialValues(<String, String>{});
    storage = SecureStorageService(const FlutterSecureStorage());
    port = _FakeMessagingPort();
    coordinator = FirebaseMessagingCoordinator(
      messaging: port,
      registerDevice: ({required fcmToken, required pushEnabled}) async {
        registrations.add(<String, Object>{
          'token': fcmToken,
          'pushEnabled': pushEnabled,
        });
      },
      storage: storage,
      isAuthenticated: () => _authenticated,
      onDeliveryOpened: (deliveryId) async {
        openedDeliveries.add(deliveryId);
      },
      onForegroundDelivery: () async {
        foregroundRefreshes++;
      },
    );
  }

  bool _authenticated;
  late final SecureStorageService storage;
  late final _FakeMessagingPort port;
  late final FirebaseMessagingCoordinator coordinator;
  final List<Map<String, Object>> registrations = [];
  final List<String> openedDeliveries = [];
  int foregroundRefreshes = 0;

  Future<void> dispose() async {
    await coordinator.dispose();
    await port.dispose();
  }
}

class _FakeMessagingPort implements MobileMessagingPort {
  final openedController = StreamController<Map<String, dynamic>>.broadcast();
  final foregroundController = StreamController<Map<String, dynamic>>.broadcast();
  final tokenRefreshController = StreamController<String>.broadcast();

  MobilePushAuthorization authorization = MobilePushAuthorization.denied;
  String? token;
  Map<String, dynamic>? initialData;
  int permissionRequests = 0;
  int settingsReads = 0;
  int tokenReads = 0;

  @override
  Stream<Map<String, dynamic>> get openedMessages => openedController.stream;

  @override
  Stream<Map<String, dynamic>> get foregroundMessages =>
      foregroundController.stream;

  @override
  Stream<String> get tokenRefresh => tokenRefreshController.stream;

  @override
  Future<Map<String, dynamic>?> getInitialMessageData() async => initialData;

  @override
  Future<MobilePushAuthorization> getAuthorizationStatus() async {
    settingsReads++;
    return authorization;
  }

  @override
  Future<String?> getToken() async {
    tokenReads++;
    return token;
  }

  @override
  Future<MobilePushAuthorization> requestPermission() async {
    permissionRequests++;
    return authorization;
  }

  Future<void> dispose() async {
    await openedController.close();
    await foregroundController.close();
    await tokenRefreshController.close();
  }
}

Future<void> _flushEvents() async {
  await Future<void>.delayed(const Duration(milliseconds: 10));
}
