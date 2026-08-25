import 'package:da_secure/networking/api_client.dart';
import 'package:dio/dio.dart';

class MobileDeviceRegistrationRequest {
  const MobileDeviceRegistrationRequest({
    required this.deviceId,
    required this.fcmToken,
    required this.platform,
    required this.appVersion,
    required this.pushEnabled,
  });

  final String deviceId;
  final String fcmToken;
  final String platform;
  final String appVersion;
  final bool pushEnabled;

  Map<String, dynamic> toJson() => <String, dynamic>{
        'deviceId': deviceId,
        'fcmToken': fcmToken,
        'platform': platform,
        'appVersion': appVersion,
        'pushEnabled': pushEnabled,
      };
}

class MobileDeviceRegistrationClient {
  MobileDeviceRegistrationClient(this.client);

  final ApiClient client;

  Future<bool> register({
    required String accessToken,
    required MobileDeviceRegistrationRequest request,
  }) async {
    final bearer = accessToken.trim();
    if (bearer.isEmpty) {
      return false;
    }
    try {
      final response = await client.dio.post<dynamic>(
        '/api/mobile/devices/register',
        data: request.toJson(),
        options: Options(headers: <String, dynamic>{
          'Authorization': 'Bearer $bearer',
          'Content-Type': 'application/json',
        }),
      );
      final status = response.statusCode ?? 0;
      return status >= 200 && status < 300;
    } on DioException {
      return false;
    }
  }
}
