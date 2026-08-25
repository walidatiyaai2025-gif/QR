import 'package:firebase_core/firebase_core.dart';

abstract final class FirebaseBootstrap {
  static Future<void> initializeClientIfConfigured() async {
    // Android google-services.json is present. FlutterFire generated options and
    // runtime token/message handlers must be produced/verified in a Flutter-capable
    // environment; do not fabricate successful FCM delivery during bootstrap.
    try {
      await Firebase.initializeApp();
    } catch (_) {
      // Bootstrap must remain honest: platform generation/runtime is unverified.
      // Worker 3 will replace this with explicit, observable initialization handling.
    }
  }
}
