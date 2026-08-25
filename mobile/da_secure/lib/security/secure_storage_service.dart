import 'dart:math';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

class SecureStorageService {
  const SecureStorageService(this.storage);
  final FlutterSecureStorage storage;

  static const sessionKey = 'mobile_session';
  static const refreshKey = 'mobile_refresh';
  static const installationIdKey = 'mobile_installation_id';
  static const pendingFcmTokenKey = 'pending_fcm_token';

  Future<String> installationId() async {
    final existing = await storage.read(key: installationIdKey);
    if (existing != null && existing.trim().isNotEmpty) {
      return existing.trim();
    }
    final random = Random.secure();
    final bytes = List<int>.generate(24, (_) => random.nextInt(256));
    final value = bytes.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
    await storage.write(key: installationIdKey, value: value);
    return value;
  }

  Future<void> savePendingFcmToken(String token) async {
    final normalized = token.trim();
    if (normalized.isEmpty) {
      await storage.delete(key: pendingFcmTokenKey);
      return;
    }
    await storage.write(key: pendingFcmTokenKey, value: normalized);
  }

  Future<String?> readPendingFcmToken() async {
    final value = await storage.read(key: pendingFcmTokenKey);
    final normalized = value?.trim();
    return normalized == null || normalized.isEmpty ? null : normalized;
  }

  Future<void> clearPendingFcmToken() => storage.delete(key: pendingFcmTokenKey);

  Future<void> clearSession() async {
    await storage.delete(key: sessionKey);
    await storage.delete(key: refreshKey);
  }
}
