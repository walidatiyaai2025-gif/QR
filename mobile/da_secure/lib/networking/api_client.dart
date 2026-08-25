import 'package:da_secure/config/app_config.dart';
import 'package:dio/dio.dart';

class ApiClient {
  ApiClient()
      : dio = Dio(BaseOptions(
          baseUrl: AppConfig.apiBaseUrl,
          connectTimeout: const Duration(seconds: 15),
          receiveTimeout: const Duration(seconds: 20),
          sendTimeout: const Duration(seconds: 20),
          headers: const {'Accept': 'application/json'},
        ));

  final Dio dio;

  // Authentication/refresh interceptors are intentionally not fabricated in bootstrap.
  // Worker 2 must connect this client to the real server-issued mobile session contract.
}
