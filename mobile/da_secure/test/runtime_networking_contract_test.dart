import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:da_secure/models/mobile_models.dart';
import 'package:da_secure/networking/api_client.dart';
import 'package:da_secure/networking/app_failure.dart';
import 'package:da_secure/repositories/auth_repository.dart';
import 'package:da_secure/repositories/device_repository.dart';
import 'package:da_secure/repositories/inbox_repository.dart';
import 'package:da_secure/security/secure_storage_service.dart';
import 'package:dio/dio.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  setUp(() {
    FlutterSecureStorage.setMockInitialValues(<String, String>{});
  });

  group('secure session storage', () {
    test('persists and restores both server credentials securely', () async {
      final fixture = _fixture();
      final session = _session();

      await fixture.storage.writeSession(session);
      final restored = await fixture.storage.readSession();

      expect(restored?.accessToken, session.accessToken);
      expect(restored?.refreshToken, session.refreshToken);
      expect(restored?.sessionId, session.sessionId);
      expect(restored?.organization.id, 7);
    });

    test('replaces an old session generation atomically', () async {
      final fixture = _fixture();
      await fixture.storage.writeSession(_session(access: 'access-one'));
      await fixture.storage.writeSession(
        _session(access: 'access-two', refresh: 'refresh-two'),
      );

      final all = await fixture.rawStorage.readAll();
      final generationKeys = all.keys
          .where((key) => key.startsWith('da_secure.session.'))
          .toList();
      expect(generationKeys, hasLength(1));
      expect((await fixture.storage.readSession())?.accessToken, 'access-two');
    });

    test('clearSession removes sensitive credentials', () async {
      final fixture = _fixture();
      await fixture.storage.writeSession(_session());
      await fixture.storage.clearSession();
      expect(await fixture.storage.readSession(), isNull);
    });

    test('malformed stored session is cleared instead of trusted', () async {
      const raw = FlutterSecureStorage();
      await raw.write(key: 'da_secure.session.active', value: 'bad');
      await raw.write(key: 'da_secure.session.bad', value: '{not-json');
      final storage = SecureStorageService(raw);

      expect(await storage.readSession(), isNull);
      expect(await raw.read(key: 'da_secure.session.active'), isNull);
    });

    test('stable device id is application generated and reused', () async {
      final fixture = _fixture();
      final first = await fixture.storage.getOrCreateDeviceId();
      final second = await fixture.storage.getOrCreateDeviceId();
      expect(first, second);
      expect(first.length, greaterThanOrEqualTo(24));
    });

    test('pending delivery stores only positive numeric identifiers', () async {
      final fixture = _fixture();
      await fixture.storage.writePendingDeliveryId('42');
      expect(await fixture.storage.readPendingDeliveryId(), '42');

      await fixture.storage.writePendingDeliveryId('-1');
      expect(await fixture.storage.readPendingDeliveryId(), isNull);
      await fixture.storage.writePendingDeliveryId('not-an-id');
      expect(await fixture.storage.readPendingDeliveryId(), isNull);
    });

    test('notification prompt state is persisted separately', () async {
      final fixture = _fixture();
      expect(await fixture.storage.notificationPermissionPrompted(), isFalse);
      await fixture.storage.markNotificationPermissionPrompted();
      expect(await fixture.storage.notificationPermissionPrompted(), isTrue);
    });

    test('biometric preference can be enabled and disabled', () async {
      final fixture = _fixture();
      expect(await fixture.storage.biometricEnabled(), isFalse);
      await fixture.storage.setBiometricEnabled(true);
      expect(await fixture.storage.biometricEnabled(), isTrue);
      await fixture.storage.setBiometricEnabled(false);
      expect(await fixture.storage.biometricEnabled(), isFalse);
    });
  });

  group('authentication repository', () {
    test('normalizes friendly Kuwait mobile formats', () {
      final fixture = _fixture();
      final auth = AuthRepository(
        client: fixture.client,
        storage: fixture.storage,
      );
      expect(auth.normalizeKuwaitMobile('5000 0001'), '+96550000001');
      expect(auth.normalizeKuwaitMobile('+965 5000 0001'), '+96550000001');
      expect(auth.normalizeKuwaitMobile('00965-5000-0001'), '+96550000001');
      expect(auth.normalizeKuwaitMobile('050000001'), '+96550000001');
    });

    test('rejects invalid mobile locally without selecting tenant', () {
      final fixture = _fixture();
      final auth = AuthRepository(
        client: fixture.client,
        storage: fixture.storage,
      );
      expect(
        () => auth.normalizeKuwaitMobile('123'),
        throwsA(
          isA<AppFailure>().having(
            (failure) => failure.code,
            'code',
            'INVALID_MOBILE',
          ),
        ),
      );
    });

    test('request OTP uses the real endpoint and normalized body', () async {
      final requests = <RequestOptions>[];
      final fixture = _fixture(
        mainHandler: (request) {
          requests.add(request);
          return _jsonResponse(_otpChallengeJson());
        },
      );
      final auth = AuthRepository(
        client: fixture.client,
        storage: fixture.storage,
      );

      final challenge = await auth.requestOtp('+965 5000 0001');

      expect(requests.single.path, '/api/mobile/auth/request-otp');
      expect(requests.single.method, 'POST');
      expect(requests.single.headers['Authorization'], isNull);
      expect(
        (requests.single.data as Map<String, dynamic>)['mobileNumber'],
        '+96550000001',
      );
      expect(challenge.challengeId, 'challenge-1');
    });

    test('malformed OTP challenge maps to safe server failure', () async {
      final fixture = _fixture(
        mainHandler: (_) => _jsonResponse(<String, Object?>{
          'challengeId': '',
          'expiresAtUtc': 'bad-date',
          'resendAvailableAtUtc': 'bad-date',
        }),
      );
      final auth = AuthRepository(
        client: fixture.client,
        storage: fixture.storage,
      );
      await expectLater(
        auth.requestOtp('50000001'),
        throwsA(
          isA<AppFailure>().having(
            (failure) => failure.code,
            'code',
            'INVALID_API_RESPONSE',
          ),
        ),
      );
    });

    test('invalid OTP format never calls the server', () async {
      var calls = 0;
      final fixture = _fixture(
        mainHandler: (_) {
          calls++;
          return _jsonResponse(<String, Object?>{});
        },
      );
      final auth = AuthRepository(
        client: fixture.client,
        storage: fixture.storage,
      );
      await expectLater(
        auth.verifyOtp(challengeId: 'challenge-1', otp: '12'),
        throwsA(isA<AppFailure>()),
      );
      expect(calls, 0);
    });

    test('successful OTP verification persists the real session', () async {
      final requests = <RequestOptions>[];
      final fixture = _fixture(
        mainHandler: (request) {
          requests.add(request);
          return _jsonResponse(_sessionJson());
        },
      );
      final auth = AuthRepository(
        client: fixture.client,
        storage: fixture.storage,
      );

      final session = await auth.verifyOtp(
        challengeId: 'challenge-1',
        otp: '123456',
      );

      expect(requests.single.path, '/api/mobile/auth/verify-otp');
      expect(requests.single.headers['Authorization'], isNull);
      expect(session.accessToken, 'access-old');
      expect(
        (await fixture.storage.readSession())?.refreshToken,
        'refresh-old',
      );
    });

    test(
      'OTP server failure does not create an authenticated session',
      () async {
        final fixture = _fixture(
          mainHandler: (_) => _jsonResponse(<String, Object?>{
            'code': 'INVALID_OTP',
            'messageArabic': 'رمز التحقق غير صحيح أو غير صالح.',
            'messageEnglish': 'The verification code is invalid.',
          }, statusCode: 400),
        );
        final auth = AuthRepository(
          client: fixture.client,
          storage: fixture.storage,
        );
        await expectLater(
          auth.verifyOtp(challengeId: 'challenge-1', otp: '123456'),
          throwsA(
            isA<AppFailure>().having(
              (failure) => failure.code,
              'code',
              'INVALID_OTP',
            ),
          ),
        );
        expect(await fixture.storage.readSession(), isNull);
      },
    );

    test('/me drives authoritative organization identity', () async {
      final fixture = _fixture(
        mainHandler: (_) => _jsonResponse(_currentUserJson(orgId: 99)),
      );
      await fixture.storage.writeSession(_session());
      final auth = AuthRepository(
        client: fixture.client,
        storage: fixture.storage,
      );

      final user = await auth.getCurrentUser();

      expect(user.organization.id, 99);
      expect(user.organization.nameEnglish, 'Authority 99');
    });

    test(
      'logout clears local session even when server revocation is offline',
      () async {
        final fixture = _fixture(
          mainHandler: (request) => throw DioException(
            requestOptions: request,
            type: DioExceptionType.connectionError,
            error: StateError('offline'),
          ),
        );
        await fixture.storage.writeSession(_session());
        final auth = AuthRepository(
          client: fixture.client,
          storage: fixture.storage,
        );

        await auth.logout();

        expect(await fixture.storage.readSession(), isNull);
      },
    );
  });

  group('bearer and refresh lifecycle', () {
    test('authenticated API injects exact Bearer authorization', () async {
      late RequestOptions captured;
      final fixture = _fixture(
        mainHandler: (request) {
          captured = request;
          return _jsonResponse(_currentUserJson());
        },
      );
      await fixture.storage.writeSession(_session());

      await fixture.client.get('/api/mobile/me');

      expect(captured.headers['Authorization'], 'Bearer access-old');
    });

    test(
      'public API call does not attach a stored bearer credential',
      () async {
        late RequestOptions captured;
        final fixture = _fixture(
          mainHandler: (request) {
            captured = request;
            return _jsonResponse(_otpChallengeJson());
          },
        );
        await fixture.storage.writeSession(_session());

        await fixture.client.post(
          '/api/mobile/auth/request-otp',
          data: const <String, String>{'mobileNumber': '+96550000001'},
          skipAuth: true,
        );

        expect(captured.headers['Authorization'], isNull);
      },
    );

    test('401 performs refresh then retries original request once', () async {
      var originalCalls = 0;
      var refreshCalls = 0;
      final fixture = _fixture(
        mainHandler: (request) {
          originalCalls++;
          if (request.headers['Authorization'] == 'Bearer access-new') {
            return _jsonResponse(_currentUserJson());
          }
          return _jsonResponse(const <String, Object?>{
            'code': 'SESSION_EXPIRED',
            'messageArabic': 'الجلسة غير صالحة.',
            'messageEnglish': 'The mobile session is invalid.',
          }, statusCode: 401);
        },
        refreshHandler: (request) async {
          refreshCalls++;
          return _jsonResponse(
            _sessionJson(access: 'access-new', refresh: 'refresh-new'),
          );
        },
      );
      await fixture.storage.writeSession(_session());

      final response = await fixture.client.get('/api/mobile/me');

      expect(response.statusCode, 200);
      expect(refreshCalls, 1);
      expect(originalCalls, 2);
      expect((await fixture.storage.readSession())?.accessToken, 'access-new');
      expect(
        (await fixture.storage.readSession())?.refreshToken,
        'refresh-new',
      );
    });

    test(
      'simultaneous authorization failures use one refresh flight',
      () async {
        var refreshCalls = 0;
        final fixture = _fixture(
          mainHandler: (request) {
            if (request.headers['Authorization'] == 'Bearer access-new') {
              return _jsonResponse(const <String, Object?>{'ok': true});
            }
            return _jsonResponse(const <String, Object?>{
              'code': 'SESSION_EXPIRED',
            }, statusCode: 401);
          },
          refreshHandler: (_) async {
            refreshCalls++;
            await Future<void>.delayed(const Duration(milliseconds: 25));
            return _jsonResponse(
              _sessionJson(access: 'access-new', refresh: 'refresh-new'),
            );
          },
        );
        await fixture.storage.writeSession(_session());

        final results = await Future.wait([
          fixture.client.get('/api/mobile/me'),
          fixture.client.get('/api/mobile/inbox'),
          fixture.client.get('/api/mobile/me'),
        ]);

        expect(results.every((response) => response.statusCode == 200), isTrue);
        expect(refreshCalls, 1);
      },
    );

    test(
      'failed refresh clears session and invokes invalidation once',
      () async {
        var invalidations = 0;
        final fixture = _fixture(
          mainHandler: (_) => _jsonResponse(const <String, Object?>{
            'code': 'SESSION_EXPIRED',
          }, statusCode: 401),
          refreshHandler: (_) => _jsonResponse(const <String, Object?>{
            'code': 'SESSION_EXPIRED',
          }, statusCode: 401),
        );
        fixture.client.onSessionInvalidated = () => invalidations++;
        await fixture.storage.writeSession(_session());

        await expectLater(
          fixture.client.get('/api/mobile/me'),
          throwsA(isA<DioException>()),
        );

        expect(await fixture.storage.readSession(), isNull);
        expect(invalidations, 1);
      },
    );

    test(
      'malformed refresh response invalidates instead of rolling back tokens',
      () async {
        final fixture = _fixture(
          refreshHandler: (_) => _jsonResponse(const <String, Object?>{
            'accessToken': 'missing-critical-fields',
          }),
        );
        await fixture.storage.writeSession(_session());

        final refreshed = await fixture.client.refreshSession();

        expect(refreshed, isNull);
        expect(await fixture.storage.readSession(), isNull);
      },
    );

    test(
      'refresh endpoint itself never enters recursive refresh loop',
      () async {
        var refreshCalls = 0;
        final fixture = _fixture(
          refreshHandler: (_) {
            refreshCalls++;
            return _jsonResponse(const <String, Object?>{
              'code': 'SESSION_EXPIRED',
            }, statusCode: 401);
          },
        );
        await fixture.storage.writeSession(_session());

        expect(await fixture.client.refreshSession(), isNull);
        expect(refreshCalls, 1);
      },
    );
  });

  group('device registration', () {
    test(
      'authenticated device registration uses exact server contract',
      () async {
        late RequestOptions captured;
        final fixture = _fixture(
          mainHandler: (request) {
            captured = request;
            return _jsonResponse(const <String, Object?>{
              'code': 'DEVICE_REGISTERED',
              'deviceId': 12,
              'pushEnabled': true,
            });
          },
        );
        await fixture.storage.writeSession(_session());
        final devices = DeviceRepository(
          client: fixture.client,
          storage: fixture.storage,
        );

        final result = await devices.register(
          fcmToken: 'fcm-test-token',
          pushEnabled: true,
        );

        expect(captured.path, '/api/mobile/devices/register');
        expect(captured.headers['Authorization'], 'Bearer access-old');
        final body = captured.data as Map<String, dynamic>;
        expect(body['fcmToken'], 'fcm-test-token');
        expect(body['platform'], 'android');
        expect(body['appVersion'], isNotEmpty);
        expect(body['deviceId'], isNotEmpty);
        expect(body.containsKey('organizationId'), isFalse);
        expect(result.deviceDatabaseId, 12);
        expect(result.pushEnabled, isTrue);
      },
    );

    test(
      'permission denial can be represented truthfully by pushEnabled false',
      () async {
        late RequestOptions captured;
        final fixture = _fixture(
          mainHandler: (request) {
            captured = request;
            return _jsonResponse(const <String, Object?>{
              'code': 'DEVICE_REGISTERED',
              'deviceId': 13,
              'pushEnabled': false,
            });
          },
        );
        await fixture.storage.writeSession(_session());
        final devices = DeviceRepository(
          client: fixture.client,
          storage: fixture.storage,
        );

        final result = await devices.register(
          fcmToken: 'fcm-test-token',
          pushEnabled: false,
        );

        expect((captured.data as Map<String, dynamic>)['pushEnabled'], isFalse);
        expect(result.pushEnabled, isFalse);
      },
    );

    test('empty FCM token never reaches the registration endpoint', () async {
      var calls = 0;
      final fixture = _fixture(
        mainHandler: (_) {
          calls++;
          return _jsonResponse(const <String, Object?>{});
        },
      );
      final devices = DeviceRepository(
        client: fixture.client,
        storage: fixture.storage,
      );

      await expectLater(
        devices.register(fcmToken: '  ', pushEnabled: true),
        throwsA(isA<AppFailure>()),
      );
      expect(calls, 0);
    });

    test('malformed device database id is not silently accepted', () async {
      final fixture = _fixture(
        mainHandler: (_) => _jsonResponse(const <String, Object?>{
          'code': 'DEVICE_REGISTERED',
          'deviceId': 0,
          'pushEnabled': true,
        }),
      );
      final devices = DeviceRepository(
        client: fixture.client,
        storage: fixture.storage,
      );
      await expectLater(
        devices.register(fcmToken: 'token', pushEnabled: true),
        throwsA(
          isA<AppFailure>().having(
            (failure) => failure.code,
            'code',
            'INVALID_API_RESPONSE',
          ),
        ),
      );
    });
  });

  group('inbox and secure reveal contracts', () {
    test('Inbox uses real server pagination and no tenant selector', () async {
      late RequestOptions captured;
      final fixture = _fixture(
        mainHandler: (request) {
          captured = request;
          return _jsonResponse(_inboxJson(items: const <Object?>[]));
        },
      );
      await fixture.storage.writeSession(_session());
      final inbox = InboxRepository(fixture.client);

      final page = await inbox.getInbox(page: 2, pageSize: 10);

      expect(captured.path, '/api/mobile/inbox');
      expect(captured.queryParameters, {'page': 2, 'pageSize': 10});
      expect(captured.queryParameters.containsKey('organizationId'), isFalse);
      expect(page.items, isEmpty);
    });

    test('Inbox maps real server delivery metadata', () async {
      final fixture = _fixture(
        mainHandler: (_) => _jsonResponse(
          _inboxJson(items: <Object?>[_deliveryJson(status: 'SUCCESS')]),
        ),
      );
      final inbox = InboxRepository(fixture.client);

      final page = await inbox.getInbox();

      expect(page.items.single.deliveryId, 42);
      expect(page.items.single.remainingReveals, 3);
      expect(page.items.single.status, 'SUCCESS');
    });

    test('delivery details preserve revoked server state', () async {
      final fixture = _fixture(
        mainHandler: (_) => _jsonResponse(<String, Object?>{
          'delivery': _deliveryJson(status: 'REVOKED'),
        }),
      );
      final details = await InboxRepository(fixture.client).getDelivery(42);
      expect(details.status, 'REVOKED');
    });

    test('delivery details preserve expired server state', () async {
      final fixture = _fixture(
        mainHandler: (_) => _jsonResponse(<String, Object?>{
          'delivery': _deliveryJson(status: 'EXPIRED'),
        }),
      );
      final details = await InboxRepository(fixture.client).getDelivery(42);
      expect(details.status, 'EXPIRED');
    });

    test(
      'wrong secure credentials map safely and do not call reveal',
      () async {
        var revealCalls = 0;
        final fixture = _fixture(
          mainHandler: (request) {
            if (request.path.endsWith('/reveal')) revealCalls++;
            return _jsonResponse(const <String, Object?>{
              'code': 'INVALID_SECURE_CREDENTIALS',
              'messageArabic': 'بيانات الاعتماد غير صحيحة.',
              'messageEnglish': 'The credentials are invalid.',
            }, statusCode: 403);
          },
        );
        final inbox = InboxRepository(fixture.client);

        await expectLater(
          inbox.authenticate(
            deliveryId: 42,
            username: 'test-user',
            password: 'test-password',
          ),
          throwsA(
            isA<AppFailure>().having(
              (failure) => failure.kind,
              'kind',
              AppFailureKind.invalidCredentials,
            ),
          ),
        );
        expect(revealCalls, 0);
      },
    );

    test(
      'secure authenticate sends credentials only to authenticate endpoint',
      () async {
        late RequestOptions captured;
        final fixture = _fixture(
          mainHandler: (request) {
            captured = request;
            return _jsonResponse(<String, Object?>{
              'code': 'SECURE_AUTHENTICATED',
              'revealToken': 'one-time-reveal-grant',
              'revealExpiresAtUtc': _future(5),
            });
          },
        );
        final grant = await InboxRepository(fixture.client).authenticate(
          deliveryId: 42,
          username: 'test-user',
          password: 'test-password',
        );

        expect(captured.path, '/api/mobile/inbox/42/authenticate');
        expect(captured.data, {
          'username': 'test-user',
          'password': 'test-password',
        });
        expect(grant.revealToken, 'one-time-reveal-grant');
      },
    );

    test(
      'reveal uses only server-issued grant and returns server count',
      () async {
        late RequestOptions captured;
        final fixture = _fixture(
          mainHandler: (request) {
            captured = request;
            return _jsonResponse(_secureMessageJson(remainingReveals: 2));
          },
        );
        final message = await InboxRepository(
          fixture.client,
        ).reveal(deliveryId: 42, revealToken: 'one-time-reveal-grant');

        expect(captured.path, '/api/mobile/inbox/42/reveal');
        expect(captured.data, {'revealToken': 'one-time-reveal-grant'});
        expect(message.remainingReveals, 2);
      },
    );

    test('text with zero attachments parses successfully', () async {
      final fixture = _fixture(
        mainHandler: (_) => _jsonResponse(_secureMessageJson()),
      );
      final message = await InboxRepository(
        fixture.client,
      ).reveal(deliveryId: 42, revealToken: 'grant');
      expect(message.contentEnglishHtml, contains('Secure message'));
      expect(message.attachments, isEmpty);
    });

    test(
      'cross-tenant or missing delivery response is handled as not found',
      () async {
        final fixture = _fixture(
          mainHandler: (_) => _jsonResponse(const <String, Object?>{
            'code': 'DELIVERY_NOT_FOUND',
            'messageArabic': 'الرسالة غير موجودة.',
            'messageEnglish': 'The delivery was not found.',
          }, statusCode: 404),
        );
        await expectLater(
          InboxRepository(fixture.client).getDelivery(999),
          throwsA(
            isA<AppFailure>().having(
              (failure) => failure.kind,
              'kind',
              AppFailureKind.notFound,
            ),
          ),
        );
      },
    );

    test('malformed delivery id in server payload is rejected', () async {
      final fixture = _fixture(
        mainHandler: (_) => _jsonResponse(
          _inboxJson(
            items: <Object?>[
              <String, Object?>{
                ..._deliveryJson(status: 'SUCCESS'),
                'deliveryId': 0,
              },
            ],
          ),
        ),
      );
      await expectLater(
        InboxRepository(fixture.client).getInbox(),
        throwsA(
          isA<AppFailure>().having(
            (failure) => failure.code,
            'code',
            'INVALID_API_RESPONSE',
          ),
        ),
      );
    });

    test(
      'malformed optional date is rejected instead of converted to null',
      () {
        expect(
          () => InboxItem.fromJson(<String, Object?>{
            ..._deliveryJson(status: 'SUCCESS'),
            'expiresAtUtc': 'not-a-date',
          }),
          throwsFormatException,
        );
      },
    );
  });

  group('safe application failure model', () {
    test(
      'rate-limit envelope keeps retry timing without raw exception text',
      () {
        final request = RequestOptions(path: '/api/mobile/auth/request-otp');
        final failure = AppFailure.fromDio(
          DioException(
            requestOptions: request,
            type: DioExceptionType.badResponse,
            response: Response<dynamic>(
              requestOptions: request,
              statusCode: 429,
              data: const <String, Object?>{
                'error': <String, Object?>{
                  'code': 'OTP_RESEND_COOLDOWN',
                  'messageArabic': 'انتظر.',
                  'messageEnglish': 'Wait.',
                },
                'retryAfterSeconds': 30,
              },
            ),
          ),
        );
        expect(failure.kind, AppFailureKind.rateLimited);
        expect(failure.retryAfterSeconds, 30);
        expect(failure.messageEnglish, 'Wait.');
      },
    );

    test('connection timeout maps to localized timeout failure', () {
      final failure = AppFailure.fromDio(
        DioException(
          requestOptions: RequestOptions(path: '/api/mobile/me'),
          type: DioExceptionType.connectionTimeout,
        ),
      );
      expect(failure.kind, AppFailureKind.timeout);
      expect(failure.code, 'TIMEOUT');
    });

    test('TLS certificate failure never exposes a trust-all fallback', () {
      final failure = AppFailure.fromDio(
        DioException(
          requestOptions: RequestOptions(path: '/api/mobile/me'),
          type: DioExceptionType.badCertificate,
        ),
      );
      expect(failure.code, 'TLS_ERROR');
      expect(failure.kind, AppFailureKind.network);
    });

    test('401 without server text uses safe localized session message', () {
      final request = RequestOptions(path: '/api/mobile/me');
      final failure = AppFailure.fromDio(
        DioException(
          requestOptions: request,
          type: DioExceptionType.badResponse,
          response: Response<dynamic>(
            requestOptions: request,
            statusCode: 401,
            data: const <String, Object?>{},
          ),
        ),
      );
      expect(failure.kind, AppFailureKind.unauthorized);
      expect(failure.messageEnglish, contains('session'));
    });
  });
}

class _Fixture {
  const _Fixture({
    required this.rawStorage,
    required this.storage,
    required this.client,
  });

  final FlutterSecureStorage rawStorage;
  final SecureStorageService storage;
  final ApiClient client;
}

_Fixture _fixture({
  _AdapterHandler? mainHandler,
  _AdapterHandler? refreshHandler,
}) {
  const rawStorage = FlutterSecureStorage();
  final storage = SecureStorageService(rawStorage);
  final mainDio = Dio(BaseOptions(baseUrl: 'https://testapi.da.gov.kw'));
  final refreshDio = Dio(BaseOptions(baseUrl: 'https://testapi.da.gov.kw'));
  mainDio.httpClientAdapter = _StubAdapter(
    mainHandler ?? (_) => _jsonResponse(const <String, Object?>{}),
  );
  refreshDio.httpClientAdapter = _StubAdapter(
    refreshHandler ?? (_) => _jsonResponse(_sessionJson()),
  );
  final client = ApiClient(
    storage: storage,
    dio: mainDio,
    refreshDio: refreshDio,
  );
  return _Fixture(rawStorage: rawStorage, storage: storage, client: client);
}

typedef _AdapterHandler =
    FutureOr<ResponseBody> Function(RequestOptions request);

class _StubAdapter implements HttpClientAdapter {
  _StubAdapter(this.handler);

  final _AdapterHandler handler;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    return handler(options);
  }

  @override
  void close({bool force = false}) {}
}

ResponseBody _jsonResponse(Object? data, {int statusCode = 200}) =>
    ResponseBody.fromString(
      jsonEncode(data),
      statusCode,
      headers: <String, List<String>>{
        Headers.contentTypeHeader: <String>['application/json; charset=utf-8'],
      },
    );

MobileSession _session({
  String access = 'access-old',
  String refresh = 'refresh-old',
}) => MobileSession.fromJson(_sessionJson(access: access, refresh: refresh));

Map<String, Object?> _sessionJson({
  String access = 'access-old',
  String refresh = 'refresh-old',
}) => <String, Object?>{
  'code': 'AUTHENTICATED',
  'accessToken': access,
  'accessExpiresAtUtc': _future(30),
  'refreshToken': refresh,
  'refreshExpiresAtUtc': _future(1440),
  'sessionId': 'session-1',
  'organization': <String, Object?>{
    'id': 7,
    'nameArabic': 'جهة اختبار',
    'nameEnglish': 'Test Authority',
  },
};

Map<String, Object?> _otpChallengeJson() => <String, Object?>{
  'code': 'OTP_REQUEST_ACCEPTED',
  'challengeId': 'challenge-1',
  'expiresAtUtc': _future(5),
  'resendAvailableAtUtc': _future(1),
};

Map<String, Object?> _currentUserJson({int orgId = 7}) => <String, Object?>{
  'organization': <String, Object?>{
    'id': orgId,
    'nameArabic': 'جهة $orgId',
    'nameEnglish': 'Authority $orgId',
  },
  'session': <String, Object?>{
    'sessionId': 'session-1',
    'accessExpiresAtUtc': _future(30),
    'refreshExpiresAtUtc': _future(1440),
  },
  'registeredDeviceCount': 1,
};

Map<String, Object?> _inboxJson({required List<Object?> items}) =>
    <String, Object?>{
      'headingArabic': 'لديك رسالة جديدة اضغط هنا لاستعراض الرسالة',
      'headingEnglish': 'You have a new message. Tap here to view it.',
      'page': 1,
      'pageSize': 20,
      'totalCount': items.length,
      'items': items,
    };

Map<String, Object?> _deliveryJson({required String status}) =>
    <String, Object?>{
      'deliveryId': 42,
      'sentAtUtc': _past(1),
      'expiresAtUtc': _future(60),
      'firstRevealedAtUtc': null,
      'remainingReveals': 3,
      'status': status,
    };

Map<String, Object?> _secureMessageJson({int remainingReveals = 2}) =>
    <String, Object?>{
      'code': 'SECURE_MESSAGE_REVEALED',
      'headingArabic': 'لديك رسالة جديدة اضغط هنا لاستعراض الرسالة',
      'headingEnglish': 'You have a new message. Tap here to view it.',
      'contentArabicHtml': '<p>رسالة آمنة</p>',
      'contentEnglishHtml': '<p>Secure message</p>',
      'sentAtUtc': _past(1),
      'expiresAtUtc': _future(60),
      'remainingReveals': remainingReveals,
      'firstRevealedAtUtc': DateTime.now().toUtc().toIso8601String(),
      'attachments': const <Object?>[],
    };

String _future(int minutes) =>
    DateTime.now().toUtc().add(Duration(minutes: minutes)).toIso8601String();

String _past(int minutes) => DateTime.now()
    .toUtc()
    .subtract(Duration(minutes: minutes))
    .toIso8601String();
