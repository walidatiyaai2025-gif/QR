import 'dart:async';

import 'package:da_secure/config/app_config.dart';
import 'package:da_secure/models/mobile_models.dart';
import 'package:da_secure/networking/app_failure.dart';
import 'package:da_secure/security/secure_storage_service.dart';
import 'package:dio/dio.dart';

class ApiClient {
  ApiClient({required this.storage, Dio? dio, Dio? refreshDio})
    : dio = dio ?? Dio(_options()),
      _refreshDio = refreshDio ?? Dio(_options()) {
    this.dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: _authorize,
        onError: _handleAuthorizationFailure,
      ),
    );
  }

  final SecureStorageService storage;
  final Dio dio;
  final Dio _refreshDio;

  Future<MobileSession?>? _refreshInFlight;
  FutureOr<void> Function()? onSessionInvalidated;

  static BaseOptions _options() => BaseOptions(
    baseUrl: AppConfig.apiBaseUrl,
    connectTimeout: const Duration(seconds: 15),
    receiveTimeout: const Duration(seconds: 20),
    sendTimeout: const Duration(seconds: 20),
    headers: const {
      'Accept': 'application/json',
      'Content-Type': 'application/json',
    },
  );

  Future<void> _authorize(
    RequestOptions options,
    RequestInterceptorHandler handler,
  ) async {
    if (options.extra['skipAuth'] == true) {
      handler.next(options);
      return;
    }

    final session = await storage.readSession();
    if (session != null && session.accessToken.isNotEmpty) {
      options.headers['Authorization'] = 'Bearer ${session.accessToken}';
    }
    handler.next(options);
  }

  Future<void> _handleAuthorizationFailure(
    DioException error,
    ErrorInterceptorHandler handler,
  ) async {
    final request = error.requestOptions;
    final canRefresh =
        error.response?.statusCode == 401 &&
        request.extra['skipAuth'] != true &&
        request.extra['skipRefresh'] != true &&
        request.extra['retriedAfterRefresh'] != true &&
        request.path != '/api/mobile/auth/refresh';

    if (!canRefresh) {
      handler.next(error);
      return;
    }

    try {
      final refreshed = await refreshSession();
      if (refreshed == null) {
        handler.next(error);
        return;
      }

      request.extra['retriedAfterRefresh'] = true;
      request.headers['Authorization'] = 'Bearer ${refreshed.accessToken}';
      final response = await dio.fetch<dynamic>(request);
      handler.resolve(response);
    } on DioException catch (refreshError) {
      handler.next(refreshError);
    } on Object {
      handler.next(error);
    }
  }

  Future<MobileSession?> refreshSession() async {
    final active = _refreshInFlight;
    if (active != null) return active;

    final future = _performRefresh();
    _refreshInFlight = future;
    try {
      return await future;
    } finally {
      if (identical(_refreshInFlight, future)) {
        _refreshInFlight = null;
      }
    }
  }

  Future<MobileSession?> _performRefresh() async {
    final current = await storage.readSession();
    if (current == null || current.refreshExpired) {
      await _invalidateSession();
      return null;
    }

    try {
      final response = await _refreshDio.post<dynamic>(
        '/api/mobile/auth/refresh',
        data: {'refreshToken': current.refreshToken},
        options: Options(extra: const {'skipAuth': true, 'skipRefresh': true}),
      );
      final data = _jsonMap(response.data);
      final refreshed = MobileSession.fromJson(data);
      await storage.writeSession(refreshed);
      return refreshed;
    } on DioException catch (error) {
      if (error.response?.statusCode == 401) {
        await _invalidateSession();
        return null;
      }
      rethrow;
    } on FormatException {
      await _invalidateSession();
      return null;
    }
  }

  Future<void> _invalidateSession() async {
    await storage.clearSession();
    await onSessionInvalidated?.call();
  }

  Future<Response<dynamic>> get(
    String path, {
    Map<String, dynamic>? queryParameters,
    CancelToken? cancelToken,
    bool skipAuth = false,
  }) {
    return dio.get<dynamic>(
      path,
      queryParameters: queryParameters,
      cancelToken: cancelToken,
      options: Options(extra: {'skipAuth': skipAuth}),
    );
  }

  Future<Response<dynamic>> post(
    String path, {
    Object? data,
    CancelToken? cancelToken,
    bool skipAuth = false,
  }) {
    return dio.post<dynamic>(
      path,
      data: data,
      cancelToken: cancelToken,
      options: Options(extra: {'skipAuth': skipAuth}),
    );
  }

  static AppFailure mapError(DioException error) => AppFailure.fromDio(error);

  static Map<String, dynamic> jsonMap(dynamic value) => _jsonMap(value);
}

Map<String, dynamic> _jsonMap(dynamic value) {
  if (value is Map<String, dynamic>) return value;
  if (value is Map) {
    return value.map((key, item) => MapEntry(key.toString(), item));
  }
  throw const FormatException('Expected JSON object.');
}
