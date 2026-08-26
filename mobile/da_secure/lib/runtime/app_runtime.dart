import 'dart:async';
import 'dart:ui';

import 'package:da_secure/firebase/firebase_messaging_coordinator.dart';
import 'package:da_secure/models/mobile_models.dart';
import 'package:da_secure/networking/api_client.dart';
import 'package:da_secure/networking/app_failure.dart';
import 'package:da_secure/presentation/mobile_ui_contracts.dart';
import 'package:da_secure/repositories/auth_repository.dart';
import 'package:da_secure/repositories/inbox_repository.dart';
import 'package:da_secure/routing/app_navigation_state.dart';
import 'package:da_secure/security/secure_storage_service.dart';
import 'package:da_secure/services/biometric_service.dart';
import 'package:dio/dio.dart';
import 'package:intl/intl.dart';

class AppRuntime extends AppNavigationState {
  AppRuntime({
    required this.auth,
    required this.inbox,
    required this.storage,
    required this.biometrics,
    required this.client,
  }) {
    client.onSessionInvalidated = invalidateSessionPreservingPending;
  }

  final AuthRepository auth;
  final InboxRepository inbox;
  final SecureStorageService storage;
  final BiometricService biometrics;
  final ApiClient client;

  FirebaseMessagingCoordinator? _messaging;
  bool _booting = true;
  AppFailure? _bootFailure;
  CurrentUser? _currentUser;
  OtpChallenge? _otpChallenge;
  String? _requestedMobile;
  Timer? _resendTimer;
  CancelToken? _inboxCancel;

  MobileNumberUiState _mobileNumberState = const MobileNumberUiState();
  OtpUiState _otpState = const OtpUiState();
  BiometricUiState _biometricState = const BiometricUiState();
  InboxUiState _inboxState = const InboxUiState(phase: UiPhase.loading);

  final Map<int, SecureLoginUiState> _secureLoginStates = {};
  final Map<int, SecureMessageUiState> _secureMessageStates = {};
  final Map<int, CancelToken> _deliveryCancelTokens = {};

  bool get isBooting => _booting;
  AppFailure? get bootFailure => _bootFailure;
  MobileNumberUiState get mobileNumberState => _mobileNumberState;
  OtpUiState get otpState => _otpState;
  BiometricUiState get biometricState => _biometricState;
  InboxUiState get inboxState => _inboxState;
  CurrentUser? get currentUser => _currentUser;
  bool get isArabic =>
      PlatformDispatcher.instance.locale.languageCode.toLowerCase() != 'en';

  void attachMessaging(FirebaseMessagingCoordinator messaging) {
    _messaging = messaging;
  }

  Future<void> bootstrap() async {
    _booting = true;
    _bootFailure = null;
    notifyListeners();

    try {
      final pending = await storage.readPendingDeliveryId();
      if (pending != null) rememberPendingDelivery(pending);

      await _messaging?.start();
      final session = await storage.readSession();
      if (session == null || session.refreshExpired) {
        if (session != null) await storage.clearSession();
        _booting = false;
        notifyListeners();
        return;
      }

      // Server validation remains authoritative. Biometric unlock is only a
      // local convenience gate applied after /me proves the stored session is
      // still valid; it never creates or refreshes server authentication.
      _currentUser = await auth.getCurrentUser();
      if (await storage.biometricEnabled()) {
        var unlocked = false;
        try {
          unlocked = await biometrics.unlockExistingSession(arabic: isArabic);
        } on Object {
          unlocked = false;
        }
        if (!unlocked) {
          // Fail closed locally. The user can establish a fresh mobile session
          // through OTP, and can then choose whether to re-enable biometrics.
          await storage.clearSession();
          _currentUser = null;
          _booting = false;
          await invalidateSessionPreservingPending();
          return;
        }
      }

      completeAuthentication();
      _booting = false;
      notifyListeners();

      await Future.wait<void>([
        refreshInbox(),
        _registerDeviceNonFatal(requestPermission: false),
      ]);
    } on AppFailure catch (failure) {
      if (failure.kind == AppFailureKind.unauthorized) {
        await storage.clearSession();
        _currentUser = null;
        _booting = false;
        invalidateSessionPreservingPending();
        return;
      }
      _bootFailure = failure;
      _booting = false;
      notifyListeners();
    } on Object {
      _bootFailure = const AppFailure(
        kind: AppFailureKind.unknown,
        code: 'BOOTSTRAP_ERROR',
        messageArabic: 'تعذر بدء التطبيق. حاول مرة أخرى.',
        messageEnglish: 'The app could not start. Try again.',
      );
      _booting = false;
      notifyListeners();
    }
  }

  Future<bool> requestOtp(String mobileNumber) async {
    if (_mobileNumberState.isSubmitting) return false;
    _mobileNumberState = const MobileNumberUiState(isSubmitting: true);
    notifyListeners();

    try {
      final challenge = await auth.requestOtp(mobileNumber);
      _otpChallenge = challenge;
      _requestedMobile = auth.normalizeKuwaitMobile(mobileNumber);
      _mobileNumberState = const MobileNumberUiState();
      _applyChallengeCountdown(challenge);
      markOtpChallengeIssued();
      return true;
    } on AppFailure catch (failure) {
      _mobileNumberState = MobileNumberUiState(
        errorMessage: failure.messageFor(isArabic),
      );
      notifyListeners();
      return false;
    }
  }

  Future<bool> resendOtp() async {
    if (_otpState.isSubmitting || _otpState.resendSeconds > 0) return false;
    final mobile = _requestedMobile;
    if (mobile == null) return false;

    _otpState = const OtpUiState(isSubmitting: true);
    notifyListeners();
    try {
      final challenge = await auth.requestOtp(mobile);
      _otpChallenge = challenge;
      _applyChallengeCountdown(challenge);
      return true;
    } on AppFailure catch (failure) {
      _otpState = OtpUiState(errorMessage: failure.messageFor(isArabic));
      notifyListeners();
      return false;
    }
  }

  Future<bool> verifyOtp(String otp) async {
    if (_otpState.isSubmitting) return false;
    final challenge = _otpChallenge;
    if (challenge == null) {
      _otpState = OtpUiState(
        errorMessage: isArabic
            ? 'انتهت جلسة التحقق. اطلب رمزًا جديدًا.'
            : 'The verification challenge is unavailable. Request a new code.',
      );
      notifyListeners();
      return false;
    }

    _otpState = OtpUiState(
      isSubmitting: true,
      resendSeconds: _otpState.resendSeconds,
    );
    notifyListeners();

    try {
      await auth.verifyOtp(challengeId: challenge.challengeId, otp: otp);
      _currentUser = await auth.getCurrentUser();
      _otpState = const OtpUiState();
      _resendTimer?.cancel();
      markOtpVerified();
      return true;
    } on AppFailure catch (failure) {
      _otpState = OtpUiState(
        resendSeconds: _otpState.resendSeconds,
        errorMessage: failure.messageFor(isArabic),
      );
      notifyListeners();
      return false;
    }
  }

  Future<bool> enableBiometrics() async {
    if (_biometricState.isBusy) return false;
    _biometricState = const BiometricUiState(isBusy: true);
    notifyListeners();

    try {
      final enabled = await biometrics.enableForExistingSession(
        arabic: isArabic,
      );
      if (!enabled) {
        _biometricState = BiometricUiState(
          errorMessage: isArabic
              ? 'تعذر تفعيل البصمة على هذا الجهاز.'
              : 'Biometrics could not be enabled on this device.',
        );
        notifyListeners();
        return false;
      }
      await storage.setBiometricEnabled(true);
      _biometricState = const BiometricUiState();
      await finishAuthentication();
      return true;
    } on Object {
      _biometricState = BiometricUiState(
        errorMessage: isArabic
            ? 'تعذر تفعيل البصمة على هذا الجهاز.'
            : 'Biometrics could not be enabled on this device.',
      );
      notifyListeners();
      return false;
    }
  }

  Future<void> skipBiometrics() async {
    if (_biometricState.isBusy) return;
    await storage.setBiometricEnabled(false);
    await finishAuthentication();
  }

  Future<void> finishAuthentication() async {
    completeAuthentication();
    notifyListeners();
    await Future.wait<void>([
      refreshInbox(),
      _registerDeviceNonFatal(requestPermission: true),
    ]);
  }

  Future<void> refreshInbox() async {
    if (!isAuthenticated) return;
    _inboxCancel?.cancel('Superseded inbox request.');
    final cancel = CancelToken();
    _inboxCancel = cancel;
    _inboxState = InboxUiState(
      phase: UiPhase.loading,
      organizationName: _organizationName(),
    );
    notifyListeners();

    try {
      final page = await inbox.getInbox(cancelToken: cancel);
      if (cancel.isCancelled) return;
      final items = page.items.map(_toInboxUi).toList();
      _inboxState = InboxUiState(
        phase: items.isEmpty ? UiPhase.empty : UiPhase.success,
        organizationName: _organizationName(),
        items: items,
      );
      notifyListeners();
    } on AppFailure catch (failure) {
      if (cancel.isCancelled) return;
      if (failure.kind == AppFailureKind.unauthorized) return;
      _inboxState = InboxUiState(
        phase: UiPhase.error,
        organizationName: _organizationName(),
        errorMessage: failure.messageFor(isArabic),
      );
      notifyListeners();
    }
  }

  Future<void> loadDelivery(String deliveryId) async {
    final id = int.tryParse(deliveryId);
    if (id == null || id <= 0 || !isAuthenticated) return;

    _deliveryCancelTokens[id]?.cancel('Superseded delivery request.');
    final cancel = CancelToken();
    _deliveryCancelTokens[id] = cancel;
    _secureLoginStates[id] = SecureLoginUiState(
      phase: SecureDeliveryUiPhase.loading,
      organizationName: _organizationName(),
    );
    notifyListeners();

    try {
      final details = await inbox.getDelivery(id, cancelToken: cancel);
      if (cancel.isCancelled) return;
      _secureLoginStates[id] = _loginStateFromDetails(details);
      notifyListeners();
    } on AppFailure catch (failure) {
      if (cancel.isCancelled) return;
      _secureLoginStates[id] = _loginStateFromFailure(failure);
      notifyListeners();
    }
  }

  SecureLoginUiState secureLoginState(String deliveryId) {
    final id = int.tryParse(deliveryId);
    if (id == null) {
      return SecureLoginUiState(
        phase: SecureDeliveryUiPhase.error,
        errorMessage: isArabic
            ? 'معرّف الرسالة غير صالح.'
            : 'Invalid delivery identifier.',
      );
    }
    return _secureLoginStates[id] ??
        SecureLoginUiState(
          phase: SecureDeliveryUiPhase.loading,
          organizationName: _organizationName(),
        );
  }

  SecureMessageUiState secureMessageState(String deliveryId) {
    final id = int.tryParse(deliveryId);
    if (id == null) {
      return SecureMessageUiState(
        phase: SecureDeliveryUiPhase.error,
        errorMessage: isArabic
            ? 'معرّف الرسالة غير صالح.'
            : 'Invalid delivery identifier.',
      );
    }
    return _secureMessageStates[id] ??
        SecureMessageUiState(
          phase: SecureDeliveryUiPhase.error,
          errorMessage: isArabic
              ? 'يجب تسجيل الدخول للرسالة قبل عرض المحتوى.'
              : 'Sign in to the secure message before viewing its content.',
        );
  }

  bool hasRevealedMessage(String deliveryId) {
    final id = int.tryParse(deliveryId);
    return id != null &&
        _secureMessageStates[id]?.phase == SecureDeliveryUiPhase.success;
  }

  Future<bool> authenticateAndReveal({
    required String deliveryId,
    required String username,
    required String password,
  }) async {
    final id = int.tryParse(deliveryId);
    if (id == null || id <= 0) return false;
    final current = _secureLoginStates[id];
    if (current?.phase == SecureDeliveryUiPhase.submitting) return false;

    _secureLoginStates[id] = SecureLoginUiState(
      phase: SecureDeliveryUiPhase.submitting,
      organizationName: _organizationName(),
    );
    notifyListeners();

    try {
      final grant = await inbox.authenticate(
        deliveryId: id,
        username: username,
        password: password,
      );
      final message = await inbox.reveal(
        deliveryId: id,
        revealToken: grant.revealToken,
      );
      _secureLoginStates[id] = SecureLoginUiState(
        phase: SecureDeliveryUiPhase.success,
        organizationName: _organizationName(),
      );
      _secureMessageStates[id] = _toSecureMessageUi(message);
      await clearPendingDeliveryIfMatches(deliveryId);
      await refreshInbox();
      notifyListeners();
      return true;
    } on AppFailure catch (failure) {
      _secureLoginStates[id] = _loginStateFromFailure(failure);
      notifyListeners();
      return false;
    }
  }

  Future<void> handlePushOpened(String deliveryId) async {
    final id = int.tryParse(deliveryId);
    if (id == null || id <= 0) return;
    final normalized = id.toString();
    await storage.writePendingDeliveryId(normalized);
    rememberPendingDelivery(normalized);
  }

  Future<void> clearPendingDeliveryIfMatches(String deliveryId) async {
    if (pendingDeliveryId != deliveryId) return;
    clearPendingDelivery();
    await storage.writePendingDeliveryId(null);
  }

  Future<void> logout() async {
    await auth.logout();
    _currentUser = null;
    _otpChallenge = null;
    _requestedMobile = null;
    _secureLoginStates.clear();
    _secureMessageStates.clear();
    _inboxState = const InboxUiState(phase: UiPhase.empty);
    await storage.writePendingDeliveryId(null);
    signOut();
  }

  Future<void> invalidateSessionPreservingPending() async {
    final pending = pendingDeliveryId ?? await storage.readPendingDeliveryId();
    _currentUser = null;
    _secureLoginStates.clear();
    _secureMessageStates.clear();
    signOut();
    if (pending != null) rememberPendingDelivery(pending);
    notifyListeners();
  }

  Future<void> _registerDeviceNonFatal({
    required bool requestPermission,
  }) async {
    try {
      await _messaging?.registerAuthenticatedDevice(
        requestPermissionIfNeeded: requestPermission,
      );
    } on Object {
      // Push registration is non-fatal. Inbox/API remain usable and token
      // rotation will retry registration when Firebase supplies a token.
    }
  }

  void _applyChallengeCountdown(OtpChallenge challenge) {
    _resendTimer?.cancel();

    void update() {
      final seconds = challenge.resendAvailableAtUtc
          .difference(DateTime.now().toUtc())
          .inSeconds;
      _otpState = OtpUiState(resendSeconds: seconds > 0 ? seconds : 0);
      notifyListeners();
      if (seconds <= 0) _resendTimer?.cancel();
    }

    update();
    _resendTimer = Timer.periodic(const Duration(seconds: 1), (_) => update());
  }

  InboxDeliveryUiModel _toInboxUi(InboxItem item) => InboxDeliveryUiModel(
    deliveryId: item.deliveryId.toString(),
    sentLabel: _formatDate(item.sentAtUtc),
    expiryLabel: _formatDate(item.expiresAtUtc),
    remainingRevealsLabel: item.remainingReveals?.toString(),
    status: item.status,
  );

  SecureLoginUiState _loginStateFromDetails(DeliveryDetails details) {
    final phase = switch (details.status) {
      'EXPIRED' => SecureDeliveryUiPhase.expired,
      'REVOKED' => SecureDeliveryUiPhase.revoked,
      'LIMITREACHED' => SecureDeliveryUiPhase.limitReached,
      'LIMIT_REACHED' => SecureDeliveryUiPhase.limitReached,
      'SUCCESS' => SecureDeliveryUiPhase.ready,
      'ACTIVE' => SecureDeliveryUiPhase.ready,
      _ => SecureDeliveryUiPhase.error,
    };
    return SecureLoginUiState(
      phase: phase,
      organizationName: _organizationName(),
      errorMessage: phase == SecureDeliveryUiPhase.error
          ? (isArabic ? 'الرسالة غير متاحة.' : 'The message is unavailable.')
          : null,
    );
  }

  SecureLoginUiState _loginStateFromFailure(AppFailure failure) {
    final phase = switch (failure.kind) {
      AppFailureKind.expiredDelivery => SecureDeliveryUiPhase.expired,
      AppFailureKind.revokedDelivery => SecureDeliveryUiPhase.revoked,
      AppFailureKind.accessLimit => SecureDeliveryUiPhase.limitReached,
      AppFailureKind.invalidCredentials =>
        SecureDeliveryUiPhase.authenticationFailure,
      _ => SecureDeliveryUiPhase.error,
    };
    return SecureLoginUiState(
      phase: phase,
      organizationName: _organizationName(),
      errorMessage: failure.messageFor(isArabic),
    );
  }

  SecureMessageUiState _toSecureMessageUi(SecureMessage message) =>
      SecureMessageUiState(
        phase: SecureDeliveryUiPhase.success,
        organizationName: _organizationName(),
        bodyHtml: message.contentFor(isArabic),
        remainingRevealsLabel: message.remainingReveals?.toString(),
        expiryLabel: _formatDate(message.expiresAtUtc),
        attachments: const <AttachmentUiModel>[],
      );

  String? _organizationName() =>
      _currentUser?.organization.displayName(isArabic);

  String? _formatDate(DateTime? value) {
    if (value == null) return null;
    return DateFormat('yyyy-MM-dd HH:mm').format(value.toLocal());
  }

  @override
  void dispose() {
    _resendTimer?.cancel();
    _inboxCancel?.cancel('Runtime disposed.');
    for (final token in _deliveryCancelTokens.values) {
      token.cancel('Runtime disposed.');
    }
    super.dispose();
  }
}
