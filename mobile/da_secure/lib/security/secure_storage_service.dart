import 'package:flutter_secure_storage/flutter_secure_storage.dart';

abstract interface class MobileSessionStorage {
  Future<String?> readAccessToken();
  Future<String?> readRefreshToken();
  Future<void> writeSession({
    required String accessToken,
    required String refreshToken,
  });
  Future<void> clearSession();
  Future<String?> readDeviceId();
  Future<void> writeDeviceId(String deviceId);
  Future<String?> readPendingFcmToken();
  Future<void> writePendingFcmToken(String token);
  Future<void> clearPendingFcmToken();
}

class SecureStorageService implements MobileSessionStorage {
  const SecureStorageService(this.storage);

  final FlutterSecureStorage storage;

  static const sessionKey = 'mobile_session';
  static const refreshKey = 'mobile_refresh';
  static const deviceIdKey = 'mobile_device_id';
  static const pendingFcmTokenKey = 'pending_fcm_token';

  @override
  Future<String?> readAccessToken() => storage.read(key: sessionKey);

  @override
  Future<String?> readRefreshToken() => storage.read(key: refreshKey);

  @override
  Future<void> writeSession({
    required String accessToken,
    required String refreshToken,
  }) async {
    final access = accessToken.trim();
    final refresh = refreshToken.trim();
    if (access.isEmpty || refresh.isEmpty) {
      throw ArgumentError('Mobile session tokens must not be empty.');
    }
    await storage.write(key: sessionKey, value: access);
    await storage.write(key: refreshKey, value: refresh);
  }

  @override
  Future<void> clearSession() async {
    await storage.delete(key: sessionKey);
    await storage.delete(key: refreshKey);
  }

  @override
  Future<String?> readDeviceId() => storage.read(key: deviceIdKey);

  @override
  Future<void> writeDeviceId(String deviceId) async {
    final normalized = deviceId.trim();
    if (normalized.isEmpty) {
      throw ArgumentError('Device id must not be empty.');
    }
    await storage.write(key: deviceIdKey, value: normalized);
  }

  @override
  Future<String?> readPendingFcmToken() => storage.read(key: pendingFcmTokenKey);

  @override
  Future<void> writePendingFcmToken(String token) async {
    final normalized = token.trim();
    if (normalized.isEmpty) {
      await clearPendingFcmToken();
      return;
    }
    await storage.write(key: pendingFcmTokenKey, value: normalized);
  }

  @override
  Future<void> clearPendingFcmToken() => storage.delete(key: pendingFcmTokenKey);
}
