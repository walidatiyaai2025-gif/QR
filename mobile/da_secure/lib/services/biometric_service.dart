import 'package:local_auth/local_auth.dart';

class BiometricService {
  BiometricService([LocalAuthentication? authentication])
    : _authentication = authentication ?? LocalAuthentication();

  final LocalAuthentication _authentication;

  Future<bool> enableForExistingSession({required bool arabic}) =>
      _authenticate(
        localizedReason: arabic
            ? 'تحقق من هويتك لتفعيل الدخول بالبصمة إلى DA Secure'
            : 'Verify your identity to enable biometric access to DA Secure',
      );

  Future<bool> unlockExistingSession({required bool arabic}) => _authenticate(
    localizedReason: arabic
        ? 'تحقق من هويتك لفتح جلسة DA Secure المحفوظة'
        : 'Verify your identity to unlock the saved DA Secure session',
  );

  Future<bool> _authenticate({required String localizedReason}) async {
    final supported = await _authentication.isDeviceSupported();
    final canCheck = await _authentication.canCheckBiometrics;
    if (!supported || !canCheck) return false;

    return _authentication.authenticate(
      localizedReason: localizedReason,
      options: const AuthenticationOptions(
        biometricOnly: true,
        stickyAuth: true,
        useErrorDialogs: true,
      ),
    );
  }
}
