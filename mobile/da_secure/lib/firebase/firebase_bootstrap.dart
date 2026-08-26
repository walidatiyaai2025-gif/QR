import 'package:da_secure/firebase/firebase_messaging_coordinator.dart';
import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';

abstract final class FirebaseBootstrap {
  static Future<void> initializeClient() async {
    // Android google-services.json is present. Initialization failures must remain
    // visible to the runtime/QA path; never silently convert a broken Firebase
    // configuration into an apparently healthy application state.
    await Firebase.initializeApp();
    FirebaseMessaging.onBackgroundMessage(firebaseMessagingBackgroundHandler);
  }
}
