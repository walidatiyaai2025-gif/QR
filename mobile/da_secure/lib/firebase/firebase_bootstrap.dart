import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';

@pragma('vm:entry-point')
Future<void> firebaseMessagingBackgroundHandler(RemoteMessage message) async {
  await Firebase.initializeApp();
  // Background delivery is routing-only. Protected content is never read here.
  PushRoutingPayload.tryParse(message.data);
}

enum FirebaseBootstrapStatus { ready, unavailable }

class FirebaseBootstrapState {
  const FirebaseBootstrapState(this.status, this.code);

  final FirebaseBootstrapStatus status;
  final String code;
  bool get isReady => status == FirebaseBootstrapStatus.ready;
}

abstract final class FirebaseBootstrap {
  static Future<FirebaseBootstrapState> initialize() async {
    final state = await runInitialization(() async {
      await Firebase.initializeApp();
    });
    if (state.isReady) {
      FirebaseMessaging.onBackgroundMessage(firebaseMessagingBackgroundHandler);
    }
    return state;
  }

  static Future<FirebaseBootstrapState> runInitialization(
    Future<void> Function() initializer,
  ) async {
    try {
      await initializer();
      return const FirebaseBootstrapState(
        FirebaseBootstrapStatus.ready,
        'FIREBASE_READY',
      );
    } catch (_) {
      return const FirebaseBootstrapState(
        FirebaseBootstrapStatus.unavailable,
        'FIREBASE_INITIALIZATION_UNAVAILABLE',
      );
    }
  }
}

class PushRoutingPayload {
  const PushRoutingPayload({
    required this.deliveryId,
    required this.category,
    required this.version,
  });

  final String deliveryId;
  final String category;
  final String version;

  static PushRoutingPayload? tryParse(Map<String, dynamic> data) {
    final deliveryId = data['deliveryId']?.toString().trim();
    final category = data['category']?.toString().trim();
    final version = data['version']?.toString().trim();
    if (deliveryId == null || deliveryId.isEmpty || deliveryId.length > 64) {
      return null;
    }
    if (category != 'delivery' && category != 'reminder') {
      return null;
    }
    if (version != '1') {
      return null;
    }
    return PushRoutingPayload(
      deliveryId: deliveryId,
      category: category!,
      version: version!,
    );
  }
}
