import 'package:da_secure/firebase/firebase_messaging_coordinator.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('safe FCM routing payload', () {
    test('accepts canonical delivery and reminder categories', () {
      final delivery = SafeDeliveryPush.tryParse({
        'deliveryId': '42',
        'category': 'delivery',
        'version': '1',
      });
      final reminder = SafeDeliveryPush.tryParse({
        'deliveryId': '42',
        'category': 'reminder',
        'version': '1',
      });

      expect(delivery?.deliveryId, '42');
      expect(reminder?.deliveryId, '42');
    });

    test('keeps legacy notification category compatibility', () {
      final push = SafeDeliveryPush.tryParse({
        'deliveryId': '42',
        'notificationCategory': 'secure_delivery',
        'version': '1',
      });

      expect(push?.deliveryId, '42');
    });

    test('rejects unexpected or sensitive custom data', () {
      for (final forbiddenKey in <String>[
        'body',
        'content',
        'otp',
        'username',
        'password',
        'accessToken',
        'refreshToken',
        'revealToken',
        'qrToken',
        'shareToken',
        'attachments',
      ]) {
        expect(
          SafeDeliveryPush.tryParse({
            'deliveryId': '42',
            'category': 'delivery',
            'version': '1',
            forbiddenKey: 'secret',
          }),
          isNull,
          reason: forbiddenKey,
        );
      }
    });

    test('rejects invalid version category and delivery id', () {
      expect(
        SafeDeliveryPush.tryParse({
          'deliveryId': '42',
          'category': 'other',
          'version': '1',
        }),
        isNull,
      );
      expect(
        SafeDeliveryPush.tryParse({
          'deliveryId': '42',
          'category': 'delivery',
          'version': '2',
        }),
        isNull,
      );
      expect(
        SafeDeliveryPush.tryParse({
          'deliveryId': '0',
          'category': 'delivery',
          'version': '1',
        }),
        isNull,
      );
    });
  });
}
