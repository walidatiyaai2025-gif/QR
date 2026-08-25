import 'package:da_secure/config/app_config.dart';
import 'package:da_secure/networking/api_client.dart';
import 'package:da_secure/networking/app_failure.dart';
import 'package:da_secure/security/secure_storage_service.dart';
import 'package:dio/dio.dart';

class DeviceRegistration {
  const DeviceRegistration({
    required this.deviceDatabaseId,
    required this.pushEnabled,
  });

  final int deviceDatabaseId;
  final bool pushEnabled;
}

class DeviceRepository {
  const DeviceRepository({required this.client, required this.storage});

  final ApiClient client;
  final SecureStorageService storage;

  Future<DeviceRegistration> register({
    required String fcmToken,
    required bool pushEnabled,
  }) async {
    if (fcmToken.trim().isEmpty) {
      throw AppFailure.validation(
        code: 'INVALID_DEVICE',
        ar: 'تعذر تسجيل الجهاز للإشعارات.',
        en: 'The device could not be registered for notifications.',
      );
    }

    final deviceId = await storage.getOrCreateDeviceId();
    try {
      final response = await client.post(
        '/api/mobile/devices/register',
        data: {
          'deviceId': deviceId,
          'fcmToken': fcmToken.trim(),
          'platform': 'android',
          'appVersion': AppConfig.appVersion,
          'pushEnabled': pushEnabled,
        },
      );
      final json = ApiClient.jsonMap(response.data);
      final databaseId = _requiredPositiveInt(json['deviceId']);
      final responsePushEnabled = json['pushEnabled'];
      if (responsePushEnabled is! bool) {
        throw const FormatException('Invalid pushEnabled.');
      }
      return DeviceRegistration(
        deviceDatabaseId: databaseId,
        pushEnabled: responsePushEnabled,
      );
    } on DioException catch (error) {
      throw ApiClient.mapError(error);
    } on FormatException {
      throw AppFailure.invalidResponse();
    }
  }
}

int _requiredPositiveInt(dynamic value) {
  final parsed = switch (value) {
    final int number => number,
    final num number
        when number.isFinite && number == number.truncateToDouble() =>
      number.toInt(),
    final String text when RegExp(r'^\d+$').hasMatch(text.trim()) => int.tryParse(
      text.trim(),
    ),
    _ => null,
  };
  if (parsed == null || parsed <= 0) {
    throw const FormatException('Invalid deviceId.');
  }
  return parsed;
}
