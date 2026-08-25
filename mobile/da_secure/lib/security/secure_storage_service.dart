import 'dart:convert';
import 'dart:math';

import 'package:da_secure/models/mobile_models.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

class SecureStorageService {
  const SecureStorageService(this.storage);

  final FlutterSecureStorage storage;

  static const _activeSessionKey = 'da_secure.session.active';
  static const _sessionPrefix = 'da_secure.session.';
  static const _deviceIdKey = 'da_secure.device.id';
  static const _pendingDeliveryKey = 'da_secure.pending.delivery';
  static const _notificationPromptedKey = 'da_secure.notifications.prompted';
  static const _biometricEnabledKey = 'da_secure.biometric.enabled';

  Future<MobileSession?> readSession() async {
    final generation = await storage.read(key: _activeSessionKey);
    if (generation == null || generation.isEmpty) return null;

    final raw = await storage.read(key: '$_sessionPrefix$generation');
    if (raw == null || raw.isEmpty) {
      await storage.delete(key: _activeSessionKey);
      return null;
    }

    try {
      final decoded = jsonDecode(raw);
      if (decoded is! Map) throw const FormatException('Invalid session JSON.');
      return MobileSession.fromJson(
        decoded.map((key, value) => MapEntry(key.toString(), value)),
      );
    } on Object {
      await clearSession();
      return null;
    }
  }

  Future<void> writeSession(MobileSession session) async {
    final previousGeneration = await storage.read(key: _activeSessionKey);
    final generation =
        '${DateTime.now().toUtc().microsecondsSinceEpoch}-${Random.secure().nextInt(1 << 32)}';
    final newKey = '$_sessionPrefix$generation';

    await storage.write(key: newKey, value: jsonEncode(session.toJson()));
    await storage.write(key: _activeSessionKey, value: generation);

    if (previousGeneration != null && previousGeneration != generation) {
      await storage.delete(key: '$_sessionPrefix$previousGeneration');
    }
  }

  Future<void> clearSession() async {
    final generation = await storage.read(key: _activeSessionKey);
    await storage.delete(key: _activeSessionKey);
    if (generation != null && generation.isNotEmpty) {
      await storage.delete(key: '$_sessionPrefix$generation');
    }
  }

  Future<String> getOrCreateDeviceId() async {
    final existing = await storage.read(key: _deviceIdKey);
    if (existing != null && existing.trim().isNotEmpty) return existing.trim();

    final random = Random.secure();
    final bytes = List<int>.generate(24, (_) => random.nextInt(256));
    final id = base64UrlEncode(bytes).replaceAll('=', '');
    await storage.write(key: _deviceIdKey, value: id);
    return id;
  }

  Future<String?> readPendingDeliveryId() async {
    final value = (await storage.read(key: _pendingDeliveryKey))?.trim();
    final id = int.tryParse(value ?? '');
    return id != null && id > 0 ? id.toString() : null;
  }

  Future<void> writePendingDeliveryId(String? deliveryId) async {
    final id = int.tryParse(deliveryId?.trim() ?? '');
    if (id == null || id <= 0) {
      await storage.delete(key: _pendingDeliveryKey);
      return;
    }
    await storage.write(key: _pendingDeliveryKey, value: id.toString());
  }

  Future<bool> notificationPermissionPrompted() async =>
      await storage.read(key: _notificationPromptedKey) == 'true';

  Future<void> markNotificationPermissionPrompted() =>
      storage.write(key: _notificationPromptedKey, value: 'true');

  Future<bool> biometricEnabled() async =>
      await storage.read(key: _biometricEnabledKey) == 'true';

  Future<void> setBiometricEnabled(bool enabled) => enabled
      ? storage.write(key: _biometricEnabledKey, value: 'true')
      : storage.delete(key: _biometricEnabledKey);
}
