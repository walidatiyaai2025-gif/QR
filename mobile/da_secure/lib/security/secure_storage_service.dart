import 'package:flutter_secure_storage/flutter_secure_storage.dart';

class SecureStorageService {
  const SecureStorageService(this.storage);
  final FlutterSecureStorage storage;

  static const sessionKey = 'mobile_session';
  static const refreshKey = 'mobile_refresh';

  Future<void> clearSession() async {
    await storage.delete(key: sessionKey);
    await storage.delete(key: refreshKey);
  }
}
