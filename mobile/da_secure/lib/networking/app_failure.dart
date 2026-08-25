import 'package:dio/dio.dart';

enum AppFailureKind {
  network,
  timeout,
  unauthorized,
  forbidden,
  rateLimited,
  validation,
  notFound,
  expiredDelivery,
  revokedDelivery,
  accessLimit,
  invalidCredentials,
  server,
  unknown,
}

class AppFailure implements Exception {
  const AppFailure({
    required this.kind,
    required this.code,
    required this.messageArabic,
    required this.messageEnglish,
    this.retryAfterSeconds,
  });

  final AppFailureKind kind;
  final String code;
  final String messageArabic;
  final String messageEnglish;
  final int? retryAfterSeconds;

  String messageFor(bool arabic) => arabic ? messageArabic : messageEnglish;

  factory AppFailure.validation({
    String code = 'VALIDATION_ERROR',
    String ar = 'تحقق من البيانات المدخلة.',
    String en = 'Check the entered information.',
  }) {
    return AppFailure(
      kind: AppFailureKind.validation,
      code: code,
      messageArabic: ar,
      messageEnglish: en,
    );
  }

  factory AppFailure.fromDio(DioException error) {
    switch (error.type) {
      case DioExceptionType.connectionTimeout:
      case DioExceptionType.sendTimeout:
      case DioExceptionType.receiveTimeout:
      case DioExceptionType.transformTimeout:
        return const AppFailure(
          kind: AppFailureKind.timeout,
          code: 'TIMEOUT',
          messageArabic: 'انتهت مهلة الاتصال. حاول مرة أخرى.',
          messageEnglish: 'The connection timed out. Try again.',
        );
      case DioExceptionType.connectionError:
        return const AppFailure(
          kind: AppFailureKind.network,
          code: 'NO_NETWORK',
          messageArabic:
              'تعذر الاتصال بالشبكة. تحقق من الاتصال وحاول مرة أخرى.',
          messageEnglish:
              'Network connection failed. Check your connection and try again.',
        );
      case DioExceptionType.badResponse:
        return _fromResponse(error.response);
      case DioExceptionType.cancel:
        return const AppFailure(
          kind: AppFailureKind.unknown,
          code: 'REQUEST_CANCELLED',
          messageArabic: 'تم إلغاء الطلب.',
          messageEnglish: 'The request was cancelled.',
        );
      case DioExceptionType.badCertificate:
        return const AppFailure(
          kind: AppFailureKind.network,
          code: 'TLS_ERROR',
          messageArabic: 'تعذر التحقق من الاتصال الآمن بالخادم.',
          messageEnglish: 'The secure server connection could not be verified.',
        );
      case DioExceptionType.unknown:
        return const AppFailure(
          kind: AppFailureKind.unknown,
          code: 'API_ERROR',
          messageArabic: 'تعذر إتمام الطلب.',
          messageEnglish: 'The request could not be completed.',
        );
    }
  }

  static AppFailure _fromResponse(Response<dynamic>? response) {
    final status = response?.statusCode ?? 0;
    final envelope = _map(response?.data);
    final nestedError = _map(envelope['error']);
    final source = nestedError.isNotEmpty ? nestedError : envelope;
    final code = _string(source['code']).isNotEmpty
        ? _string(source['code'])
        : _defaultCode(status);
    final ar = _string(source['messageArabic']);
    final en = _string(source['messageEnglish']);
    final retryAfter =
        _int(envelope['retryAfterSeconds']) ??
        _int(source['retryAfterSeconds']);
    final kind = _kindFor(status, code);

    return AppFailure(
      kind: kind,
      code: code,
      messageArabic: ar.isNotEmpty ? ar : _fallbackArabic(kind),
      messageEnglish: en.isNotEmpty ? en : _fallbackEnglish(kind),
      retryAfterSeconds: retryAfter,
    );
  }

  static AppFailureKind _kindFor(int status, String code) {
    switch (code) {
      case 'SESSION_EXPIRED':
        return AppFailureKind.unauthorized;
      case 'OTP_RATE_LIMIT':
      case 'OTP_RESEND_COOLDOWN':
      case 'OTP_ATTEMPT_LIMIT':
        return AppFailureKind.rateLimited;
      case 'INVALID_MOBILE':
      case 'INVALID_OTP':
      case 'OTP_EXPIRED':
        return AppFailureKind.validation;
      case 'DELIVERY_NOT_FOUND':
        return AppFailureKind.notFound;
      case 'DELIVERY_EXPIRED':
        return AppFailureKind.expiredDelivery;
      case 'DELIVERY_REVOKED':
        return AppFailureKind.revokedDelivery;
      case 'REVEAL_LIMIT_REACHED':
        return AppFailureKind.accessLimit;
      case 'INVALID_SECURE_CREDENTIALS':
      case 'INVALID_REVEAL_GRANT':
        return AppFailureKind.invalidCredentials;
    }

    if (status == 401) return AppFailureKind.unauthorized;
    if (status == 403) return AppFailureKind.forbidden;
    if (status == 404) return AppFailureKind.notFound;
    if (status == 429) return AppFailureKind.rateLimited;
    if (status >= 500) return AppFailureKind.server;
    if (status >= 400) return AppFailureKind.validation;
    return AppFailureKind.unknown;
  }

  static String _defaultCode(int status) {
    if (status == 401) return 'SESSION_EXPIRED';
    if (status == 403) return 'FORBIDDEN';
    if (status == 404) return 'NOT_FOUND';
    if (status == 429) return 'RATE_LIMITED';
    return 'API_ERROR';
  }

  static String _fallbackArabic(AppFailureKind kind) => switch (kind) {
    AppFailureKind.network => 'تعذر الاتصال بالشبكة.',
    AppFailureKind.timeout => 'انتهت مهلة الاتصال.',
    AppFailureKind.unauthorized => 'انتهت الجلسة. سجل الدخول مرة أخرى.',
    AppFailureKind.forbidden => 'غير مصرح بهذا الإجراء.',
    AppFailureKind.rateLimited => 'تم تجاوز عدد المحاولات مؤقتًا.',
    AppFailureKind.validation => 'تحقق من البيانات المدخلة.',
    AppFailureKind.notFound => 'العنصر المطلوب غير موجود.',
    AppFailureKind.expiredDelivery => 'انتهت صلاحية الرسالة.',
    AppFailureKind.revokedDelivery => 'تم إلغاء الرسالة.',
    AppFailureKind.accessLimit => 'تم الوصول إلى الحد الأقصى للمشاهدة.',
    AppFailureKind.invalidCredentials => 'بيانات الاعتماد غير صحيحة.',
    AppFailureKind.server => 'الخدمة غير متاحة مؤقتًا.',
    AppFailureKind.unknown => 'تعذر إتمام الطلب.',
  };

  static String _fallbackEnglish(AppFailureKind kind) => switch (kind) {
    AppFailureKind.network => 'Network connection failed.',
    AppFailureKind.timeout => 'The connection timed out.',
    AppFailureKind.unauthorized => 'The session expired. Sign in again.',
    AppFailureKind.forbidden => 'This action is not authorized.',
    AppFailureKind.rateLimited => 'Too many attempts. Try again later.',
    AppFailureKind.validation => 'Check the entered information.',
    AppFailureKind.notFound => 'The requested item was not found.',
    AppFailureKind.expiredDelivery => 'This message has expired.',
    AppFailureKind.revokedDelivery => 'This message was revoked.',
    AppFailureKind.accessLimit => 'The reveal limit has been reached.',
    AppFailureKind.invalidCredentials => 'The credentials are invalid.',
    AppFailureKind.server => 'The service is temporarily unavailable.',
    AppFailureKind.unknown => 'The request could not be completed.',
  };
}

Map<String, dynamic> _map(dynamic value) {
  if (value is Map<String, dynamic>) return value;
  if (value is Map) {
    return value.map((key, item) => MapEntry(key.toString(), item));
  }
  return const <String, dynamic>{};
}

String _string(dynamic value) => value is String ? value.trim() : '';

int? _int(dynamic value) {
  if (value is int) return value;
  if (value is num) return value.toInt();
  return int.tryParse(value?.toString() ?? '');
}
