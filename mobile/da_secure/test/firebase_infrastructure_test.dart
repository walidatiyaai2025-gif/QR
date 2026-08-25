import 'package:da_secure/firebase/firebase_bootstrap.dart';
import 'package:da_secure/networking/mobile_device_registration_client.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('Firebase bootstrap reports unavailable instead of fake ready', () async {
    final state = await FirebaseBootstrap.runInitialization(() async {
      throw StateError('test bootstrap failure');
    });

    expect(state.status, FirebaseBootstrapStatus.unavailable);
    expect(state.isReady, isFalse);
    expect(state.code, 'FIREBASE_INITIALIZATION_UNAVAILABLE');
  });

  test('Push payload accepts only safe routing contract', () {
    final payload = PushRoutingPayload.tryParse(const <String, dynamic>{
      'deliveryId': '42',
      'category': 'reminder',
      'version': '1',
      'ignored': 'never surfaced',
    });

    expect(payload, isNotNull);
    expect(payload!.deliveryId, '42');
    expect(payload.category, 'reminder');
    expect(payload.version, '1');
  });

  test('Malformed or missing delivery routing is ignored safely', () {
    expect(PushRoutingPayload.tryParse(const <String, dynamic>{}), isNull);
    expect(
      PushRoutingPayload.tryParse(const <String, dynamic>{
        'deliveryId': '',
        'category': 'delivery',
        'version': '1',
      }),
      isNull,
    );
    expect(
      PushRoutingPayload.tryParse(const <String, dynamic>{
        'deliveryId': '42',
        'category': 'custom',
        'version': '1',
      }),
      isNull,
    );
    expect(
      PushRoutingPayload.tryParse(const <String, dynamic>{
        'deliveryId': '42',
        'category': 'delivery',
        'version': '2',
      }),
      isNull,
    );
  });

  test('Device registration request matches existing Worker 2 contract', () {
    const request = MobileDeviceRegistrationRequest(
      deviceId: 'installation-id',
      fcmToken: 'fcm-token',
      platform: 'android',
      appVersion: '0.1.0',
      pushEnabled: true,
    );

    expect(request.toJson(), <String, dynamic>{
      'deviceId': 'installation-id',
      'fcmToken': 'fcm-token',
      'platform': 'android',
      'appVersion': '0.1.0',
      'pushEnabled': true,
    });
    expect(request.toJson().keys, hasLength(5));
  });
}
