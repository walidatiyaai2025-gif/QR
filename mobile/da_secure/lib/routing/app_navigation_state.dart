import 'package:flutter/foundation.dart';

enum MobileAuthStage { mobileNumber, otp, biometricOffer, authenticated }

class AppNavigationState extends ChangeNotifier {
  MobileAuthStage _stage = MobileAuthStage.mobileNumber;
  String? _pendingDeliveryId;

  MobileAuthStage get stage => _stage;
  bool get isAuthenticated => _stage == MobileAuthStage.authenticated;
  String? get pendingDeliveryId => _pendingDeliveryId;

  void markOtpChallengeIssued() {
    _stage = MobileAuthStage.otp;
    notifyListeners();
  }

  void markOtpVerified() {
    _stage = MobileAuthStage.biometricOffer;
    notifyListeners();
  }

  void completeAuthentication() {
    _stage = MobileAuthStage.authenticated;
    notifyListeners();
  }

  void signOut() {
    _stage = MobileAuthStage.mobileNumber;
    _pendingDeliveryId = null;
    notifyListeners();
  }

  void rememberPendingDelivery(String deliveryId) {
    final normalized = deliveryId.trim();
    if (normalized.isEmpty || normalized == _pendingDeliveryId) {
      return;
    }

    _pendingDeliveryId = normalized;
    notifyListeners();
  }

  void clearPendingDelivery() {
    if (_pendingDeliveryId == null) {
      return;
    }

    _pendingDeliveryId = null;
    notifyListeners();
  }

  String postAuthenticationDestination() {
    final deliveryId = _pendingDeliveryId;
    if (deliveryId == null || deliveryId.isEmpty) {
      return '/inbox';
    }

    return '/delivery/${Uri.encodeComponent(deliveryId)}/login';
  }

  String destinationForPush(String deliveryId) {
    rememberPendingDelivery(deliveryId);
    return isAuthenticated
        ? '/delivery/${Uri.encodeComponent(deliveryId)}/login'
        : '/auth/mobile';
  }
}

final appNavigationState = AppNavigationState();
