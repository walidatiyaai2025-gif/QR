import 'package:dio/dio.dart';

abstract interface class MobileDeviceRegistrationGateway {
  Future<bool> register({
    required String accessToken,
    required String deviceId,
    required String fcmToken,
    required String platform,
    required String appVersion,
  });
}

class MobileDeviceRegistrationApi implements MobileDeviceRegistrationGateway {
  const MobileDeviceRegistrationApi(this.dio);

  final Dio dio;

  @override
  Future<bool> register({
    required String accessToken,
    required String deviceId,
    required String fcmToken,
    required String platform,
    required String appVersion,
  }) async {
    final bearer = accessToken.trim();
    final device = deviceId.trim();
    final token = fcmToken.trim();
    if (bearer.isEmpty || device.isEmpty || token.isEmpty) {
      return false;
    }

    try {
      final response = await dio.post<Object?>(
        '/api/mobile/devices/register',
        data: <String, Object?>{
          'deviceId': device,
          'fcmToken': token,
          'platform': platform,
          'appVersion': appVersion,
          'pushEnabled': true,
        },
        options: Options(
          headers: <String, String>{'Authorization': 'Bearer $bearer'},
        ),
      );
      final statusCode = response.statusCode ?? 0;
      return statusCode >= 200 && statusCode < 300;
    } on DioException {
      return false;
    }
  }
}
