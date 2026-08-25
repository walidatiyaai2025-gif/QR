import 'package:da_secure/models/mobile_models.dart';
import 'package:da_secure/networking/api_client.dart';
import 'package:da_secure/security/secure_storage_service.dart';
import 'package:dio/dio.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  test('secure credential 401 never triggers mobile session refresh', () async {
    FlutterSecureStorage.setMockInitialValues({});
    const storage = SecureStorageService(FlutterSecureStorage());
    await storage.writeSession(
      MobileSession(
        accessToken: 'access-1',
        accessExpiresAtUtc: DateTime.utc(2035, 1, 1),
        refreshToken: 'refresh-1',
        refreshExpiresAtUtc: DateTime.utc(2035, 2, 1),
        sessionId: 'session-1',
        organization: const OrganizationProfile(
          id: 7,
          nameArabic: 'جهة الخادم',
          nameEnglish: 'Server Organization',
        ),
      ),
    );

    final apiDio = Dio(BaseOptions(baseUrl: 'https://testapi.da.gov.kw'));
    final refreshDio = Dio(BaseOptions(baseUrl: 'https://testapi.da.gov.kw'));
    var refreshCalls = 0;

    apiDio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) {
          final response = Response<dynamic>(
            requestOptions: options,
            statusCode: 401,
            data: {
              'error': {
                'code': 'INVALID_SECURE_CREDENTIALS',
                'messageArabic': 'بيانات الاعتماد غير صحيحة.',
                'messageEnglish': 'The credentials are invalid.',
              },
            },
          );
          handler.reject(
            DioException(
              requestOptions: options,
              response: response,
              type: DioExceptionType.badResponse,
            ),
          );
        },
      ),
    );
    refreshDio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) {
          refreshCalls += 1;
          handler.reject(
            DioException(
              requestOptions: options,
              type: DioExceptionType.unknown,
            ),
          );
        },
      ),
    );

    final client = ApiClient(
      storage: storage,
      dio: apiDio,
      refreshDio: refreshDio,
    );

    await expectLater(
      client.post(
        '/api/mobile/inbox/99/authenticate',
        data: {'username': 'u', 'password': 'bad'},
      ),
      throwsA(isA<DioException>()),
    );

    expect(refreshCalls, 0);
    expect((await storage.readSession())?.refreshToken, 'refresh-1');
  });
}
