import 'package:da_secure/models/mobile_models.dart';
import 'package:da_secure/networking/api_client.dart';
import 'package:da_secure/networking/app_failure.dart';
import 'package:da_secure/security/secure_storage_service.dart';
import 'package:dio/dio.dart';

class AuthRepository {
  const AuthRepository({required this.client, required this.storage});

  final ApiClient client;
  final SecureStorageService storage;

  String normalizeKuwaitMobile(String input) {
    var digits = input.replaceAll(RegExp(r'\D'), '');
    if (digits.startsWith('00965')) digits = digits.substring(5);
    if (digits.startsWith('965') && digits.length == 11) {
      digits = digits.substring(3);
    }
    if (digits.startsWith('0') && digits.length == 9) {
      digits = digits.substring(1);
    }
    if (!RegExp(r'^\d{8}$').hasMatch(digits)) {
      throw AppFailure.validation(
        code: 'INVALID_MOBILE',
        ar: 'رقم الهاتف غير صالح.',
        en: 'The mobile number format is invalid.',
      );
    }
    return '+965$digits';
  }

  Future<OtpChallenge> requestOtp(String mobileNumber) async {
    final normalized = normalizeKuwaitMobile(mobileNumber);
    try {
      final response = await client.post(
        '/api/mobile/auth/request-otp',
        data: {'mobileNumber': normalized},
        skipAuth: true,
      );
      return OtpChallenge.fromJson(ApiClient.jsonMap(response.data));
    } on DioException catch (error) {
      throw ApiClient.mapError(error);
    }
  }

  Future<MobileSession> verifyOtp({
    required String challengeId,
    required String otp,
  }) async {
    if (!RegExp(r'^\d{6}$').hasMatch(otp.trim())) {
      throw AppFailure.validation(
        code: 'INVALID_OTP',
        ar: 'رمز التحقق غير صحيح أو غير صالح.',
        en: 'The verification code is invalid or no longer usable.',
      );
    }

    try {
      final response = await client.post(
        '/api/mobile/auth/verify-otp',
        data: {'challengeId': challengeId, 'otp': otp.trim()},
        skipAuth: true,
      );
      final session = MobileSession.fromJson(ApiClient.jsonMap(response.data));
      await storage.writeSession(session);
      return session;
    } on DioException catch (error) {
      throw ApiClient.mapError(error);
    }
  }

  Future<CurrentUser> getCurrentUser({CancelToken? cancelToken}) async {
    try {
      final response = await client.get(
        '/api/mobile/me',
        cancelToken: cancelToken,
      );
      return CurrentUser.fromJson(ApiClient.jsonMap(response.data));
    } on DioException catch (error) {
      throw ApiClient.mapError(error);
    }
  }

  Future<MobileSession?> refresh() => client.refreshSession();

  Future<void> logout() async {
    try {
      await client.post('/api/mobile/auth/logout');
    } on DioException {
      // Local credentials are cleared even when the revocation request cannot
      // complete. The server session will still expire according to policy.
    } finally {
      await storage.clearSession();
    }
  }
}
