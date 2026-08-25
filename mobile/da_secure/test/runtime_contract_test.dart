import 'dart:io';

import 'package:da_secure/config/app_config.dart';
import 'package:da_secure/models/mobile_models.dart';
import 'package:da_secure/networking/api_client.dart';
import 'package:da_secure/networking/app_failure.dart';
import 'package:da_secure/repositories/auth_repository.dart';
import 'package:da_secure/repositories/device_repository.dart';
import 'package:da_secure/repositories/inbox_repository.dart';
import 'package:da_secure/routing/app_navigation_state.dart';
import 'package:da_secure/runtime/app_runtime.dart';
import 'package:da_secure/security/secure_storage_service.dart';
import 'package:da_secure/services/biometric_service.dart';
import 'package:dio/dio.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  late SecureStorageService storage;
  late Dio apiDio;
  late Dio refreshDio;
  late _QueueApiInterceptor api;
  late _QueueApiInterceptor refresh;
  late ApiClient client;
  late AuthRepository auth;
  late InboxRepository inbox;
  late DeviceRepository devices;

  setUp(() {
    FlutterSecureStorage.setMockInitialValues({});
    storage = const SecureStorageService(FlutterSecureStorage());
    apiDio = Dio(BaseOptions(baseUrl: AppConfig.apiBaseUrl));
    refreshDio = Dio(BaseOptions(baseUrl: AppConfig.apiBaseUrl));
    client = ApiClient(storage: storage, dio: apiDio, refreshDio: refreshDio);
    api = _QueueApiInterceptor();
    refresh = _QueueApiInterceptor();
    apiDio.interceptors.add(api);
    refreshDio.interceptors.add(refresh);
    auth = AuthRepository(client: client, storage: storage);
    inbox = InboxRepository(client);
    devices = DeviceRepository(client: client, storage: storage);
  });

  group('auth and session contracts', () {
    test(
      '01 request OTP calls the real endpoint with normalized Kuwait mobile',
      () async {
        api.enqueue(
          'POST',
          '/api/mobile/auth/request-otp',
          202,
          _challengeJson(),
        );

        final result = await auth.requestOtp('5000 0000');

        expect(result.challengeId, 'challenge-1');
        expect(api.history.single.path, '/api/mobile/auth/request-otp');
        expect(api.history.single.data, {'mobileNumber': '+96550000000'});
      },
    );

    test('02 OTP success creates and stores a real session state', () async {
      api.enqueue('POST', '/api/mobile/auth/verify-otp', 200, _sessionJson());

      final result = await auth.verifyOtp(
        challengeId: 'challenge-1',
        otp: '123456',
      );

      expect(result.sessionId, 'session-1');
      expect((await storage.readSession())?.sessionId, 'session-1');
    });

    test('03 OTP failure does not authenticate or persist session', () async {
      api.enqueue(
        'POST',
        '/api/mobile/auth/verify-otp',
        400,
        _errorJson('INVALID_OTP'),
      );

      await expectLater(
        auth.verifyOtp(challengeId: 'challenge-1', otp: '123456'),
        throwsA(isA<AppFailure>()),
      );
      expect(await storage.readSession(), isNull);
    });

    test(
      '04 access token is persisted through secure storage service',
      () async {
        await storage.writeSession(_session());
        expect((await storage.readSession())?.accessToken, 'access-1');
      },
    );

    test(
      '05 refresh token is persisted through secure storage service',
      () async {
        await storage.writeSession(_session());
        expect((await storage.readSession())?.refreshToken, 'refresh-1');
      },
    );

    test('06 authenticated request sends current bearer token', () async {
      await storage.writeSession(_session());
      api.enqueue('GET', '/api/mobile/me', 200, _meJson());

      await auth.getCurrentUser();

      expect(api.history.single.headers['Authorization'], 'Bearer access-1');
    });

    test('07 authorization failure causes one refresh and succeeds', () async {
      await storage.writeSession(_session());
      api
        ..enqueue('GET', '/api/mobile/me', 401, _errorJson('SESSION_EXPIRED'))
        ..enqueue('GET', '/api/mobile/me', 200, _meJson());
      refresh.enqueue(
        'POST',
        '/api/mobile/auth/refresh',
        200,
        _sessionJson(access: 'access-2', refresh: 'refresh-2'),
      );

      final user = await auth.getCurrentUser();

      expect(user.organization.id, 7);
      expect(refresh.history, hasLength(1));
      expect((await storage.readSession())?.accessToken, 'access-2');
    });

    test(
      '08 concurrent authorization failures use single-flight refresh',
      () async {
        await storage.writeSession(_session());
        for (var i = 0; i < 5; i++) {
          api.enqueue(
            'GET',
            '/api/mobile/me',
            401,
            _errorJson('SESSION_EXPIRED'),
          );
        }
        for (var i = 0; i < 5; i++) {
          api.enqueue('GET', '/api/mobile/me', 200, _meJson());
        }
        refresh.enqueue(
          'POST',
          '/api/mobile/auth/refresh',
          200,
          _sessionJson(access: 'access-2', refresh: 'refresh-2'),
        );

        final users = await Future.wait(
          List.generate(5, (_) => auth.getCurrentUser()),
        );

        expect(users, hasLength(5));
        expect(refresh.history, hasLength(1));
      },
    );

    test(
      '09 original request retries exactly once after successful refresh',
      () async {
        await storage.writeSession(_session());
        api
          ..enqueue('GET', '/api/mobile/me', 401, _errorJson('SESSION_EXPIRED'))
          ..enqueue('GET', '/api/mobile/me', 200, _meJson());
        refresh.enqueue(
          'POST',
          '/api/mobile/auth/refresh',
          200,
          _sessionJson(),
        );

        await auth.getCurrentUser();

        expect(
          api.history.where((r) => r.path == '/api/mobile/me'),
          hasLength(2),
        );
      },
    );

    test(
      '10 failed refresh clears reusable local session credentials',
      () async {
        await storage.writeSession(_session());
        api.enqueue(
          'GET',
          '/api/mobile/me',
          401,
          _errorJson('SESSION_EXPIRED'),
        );
        refresh.enqueue(
          'POST',
          '/api/mobile/auth/refresh',
          401,
          _errorJson('SESSION_EXPIRED'),
        );

        await expectLater(auth.getCurrentUser(), throwsA(isA<AppFailure>()));
        expect(await storage.readSession(), isNull);
      },
    );

    test(
      '11 a retried request cannot start an infinite refresh loop',
      () async {
        await storage.writeSession(_session());
        api
          ..enqueue('GET', '/api/mobile/me', 401, _errorJson('SESSION_EXPIRED'))
          ..enqueue(
            'GET',
            '/api/mobile/me',
            401,
            _errorJson('SESSION_EXPIRED'),
          );
        refresh.enqueue(
          'POST',
          '/api/mobile/auth/refresh',
          200,
          _sessionJson(),
        );

        await expectLater(auth.getCurrentUser(), throwsA(isA<AppFailure>()));

        expect(refresh.history, hasLength(1));
        expect(api.history, hasLength(2));
      },
    );

    test('12 startup restores a valid server-authorized session', () async {
      await storage.writeSession(_session());
      api
        ..enqueue('GET', '/api/mobile/me', 200, _meJson())
        ..enqueue('GET', '/api/mobile/inbox', 200, _emptyInboxJson());
      final runtime = createRuntime();

      await runtime.bootstrap();

      expect(runtime.isAuthenticated, isTrue);
      expect(runtime.currentUser?.organization.id, 7);
      runtime.dispose();
    });

    test(
      '13 startup clears a locally stored session whose refresh is expired',
      () async {
        await storage.writeSession(_session(refreshExpired: true));
        final runtime = createRuntime();

        await runtime.bootstrap();

        expect(runtime.isAuthenticated, isFalse);
        expect(await storage.readSession(), isNull);
        runtime.dispose();
      },
    );

    test(
      '14 logout calls server and clears access and refresh credentials',
      () async {
        await storage.writeSession(_session());
        api.enqueue('POST', '/api/mobile/auth/logout', 204, null);

        await auth.logout();

        expect(api.history.single.path, '/api/mobile/auth/logout');
        expect(await storage.readSession(), isNull);
      },
    );

    test(
      '15 current user identity comes from the server me endpoint',
      () async {
        await storage.writeSession(_session());
        api.enqueue('GET', '/api/mobile/me', 200, _meJson());

        final user = await auth.getCurrentUser();

        expect(user.organization.id, 7);
        expect(user.organization.nameEnglish, 'Server Organization');
      },
    );

    test(
      '16 mobile requests do not send a client-selected OrganizationId',
      () async {
        await storage.writeSession(_session());
        api.enqueue('GET', '/api/mobile/me', 200, _meJson());

        await auth.getCurrentUser();

        expect(
          api.history.single.queryParameters.containsKey('OrganizationId'),
          isFalse,
        );
        expect(api.history.single.data, isNull);
      },
    );
  });

  group('device and push contracts', () {
    test('17 device registration uses exact current backend fields', () async {
      await storage.writeSession(_session());
      api.enqueue('POST', '/api/mobile/devices/register', 200, {
        'deviceId': 51,
        'pushEnabled': true,
      });

      final result = await devices.register(
        fcmToken: 'real-fcm-token',
        pushEnabled: true,
      );
      final data = Map<String, dynamic>.from(api.history.single.data as Map);

      expect(result.deviceDatabaseId, 51);
      expect(data.keys.toSet(), {
        'deviceId',
        'fcmToken',
        'platform',
        'appVersion',
        'pushEnabled',
      });
      expect(data['fcmToken'], 'real-fcm-token');
      expect(data['platform'], 'android');
      expect(data['pushEnabled'], isTrue);
    });

    test('18 FCM token refresh is wired to backend re-registration', () async {
      final source = await _source(
        'lib/firebase/firebase_messaging_coordinator.dart',
      );
      expect(source, contains('onTokenRefresh.listen'));
      expect(source, contains('_registerToken(token'));
    });

    test(
      '19 unauthenticated FCM token rotation cannot fake registration',
      () async {
        final source = await _source(
          'lib/firebase/firebase_messaging_coordinator.dart',
        );
        expect(source, contains('if (!isAuthenticated()) return;'));
      },
    );

    test(
      '20 pushEnabled derives from real notification authorization status',
      () async {
        final source = await _source(
          'lib/firebase/firebase_messaging_coordinator.dart',
        );
        expect(source, contains('AuthorizationStatus.authorized'));
        expect(source, contains('AuthorizationStatus.provisional'));
        expect(source, contains('pushEnabled: pushEnabled'));
      },
    );

    test(
      '21 raw FCM tokens are never logged by mobile runtime sources',
      () async {
        final source = await _source(
          'lib/firebase/firebase_messaging_coordinator.dart',
        );
        expect(source, isNot(contains('print(token')));
        expect(source, isNot(contains('debugPrint(token')));
        expect(source, isNot(contains('log(token')));
      },
    );
  });

  group('inbox contracts', () {
    test('22 Inbox repository calls real API and parses deliveries', () async {
      await storage.writeSession(_session());
      api.enqueue('GET', '/api/mobile/inbox', 200, _inboxJson());

      final page = await inbox.getInbox();

      expect(page.items, hasLength(1));
      expect(page.items.single.deliveryId, 99);
      expect(api.history.single.queryParameters, {'page': 1, 'pageSize': 20});
    });

    test('23 real empty Inbox response produces an empty collection', () async {
      await storage.writeSession(_session());
      api.enqueue('GET', '/api/mobile/inbox', 200, _emptyInboxJson());

      final page = await inbox.getInbox();

      expect(page.items, isEmpty);
      expect(page.totalCount, 0);
    });

    test('24 Inbox API failure maps to application failure state', () async {
      await storage.writeSession(_session());
      api.enqueue('GET', '/api/mobile/inbox', 500, _errorJson('SERVER_ERROR'));

      await expectLater(inbox.getInbox(), throwsA(isA<AppFailure>()));
    });

    test('25 Inbox can retry after transient failure', () async {
      await storage.writeSession(_session());
      api
        ..enqueue('GET', '/api/mobile/inbox', 500, _errorJson('SERVER_ERROR'))
        ..enqueue('GET', '/api/mobile/inbox', 200, _emptyInboxJson());

      await expectLater(inbox.getInbox(), throwsA(isA<AppFailure>()));
      final retry = await inbox.getInbox();

      expect(retry.items, isEmpty);
    });

    test(
      '26 unauthorized Inbox response maps to unauthorized auth failure',
      () async {
        api.enqueue(
          'GET',
          '/api/mobile/inbox',
          401,
          _errorJson('SESSION_EXPIRED'),
        );

        await expectLater(
          inbox.getInbox(),
          throwsA(
            isA<AppFailure>().having(
              (failure) => failure.kind,
              'kind',
              AppFailureKind.unauthorized,
            ),
          ),
        );
      },
    );

    test(
      '27 delivery detail not-found is not treated as an owned delivery',
      () async {
        await storage.writeSession(_session());
        api.enqueue(
          'GET',
          '/api/mobile/inbox/99',
          404,
          _errorJson('DELIVERY_NOT_FOUND'),
        );

        await expectLater(
          inbox.getDelivery(99),
          throwsA(
            isA<AppFailure>().having(
              (f) => f.kind,
              'kind',
              AppFailureKind.notFound,
            ),
          ),
        );
      },
    );

    test(
      '28 revoked delivery is represented by revoked failure kind',
      () async {
        await storage.writeSession(_session());
        api.enqueue(
          'GET',
          '/api/mobile/inbox/99',
          410,
          _errorJson('DELIVERY_REVOKED'),
        );

        await expectLater(
          inbox.getDelivery(99),
          throwsA(
            isA<AppFailure>().having(
              (f) => f.kind,
              'kind',
              AppFailureKind.revokedDelivery,
            ),
          ),
        );
      },
    );

    test(
      '29 expired delivery is represented by expired failure kind',
      () async {
        await storage.writeSession(_session());
        api.enqueue(
          'GET',
          '/api/mobile/inbox/99',
          410,
          _errorJson('DELIVERY_EXPIRED'),
        );

        await expectLater(
          inbox.getDelivery(99),
          throwsA(
            isA<AppFailure>().having(
              (f) => f.kind,
              'kind',
              AppFailureKind.expiredDelivery,
            ),
          ),
        );
      },
    );
  });

  group('secure reveal contracts', () {
    test('30 wrong secure credentials do not call reveal endpoint', () async {
      await storage.writeSession(_session());
      api.enqueue(
        'POST',
        '/api/mobile/inbox/99/authenticate',
        401,
        _errorJson('INVALID_SECURE_CREDENTIALS'),
      );

      await expectLater(
        inbox.authenticate(deliveryId: 99, username: 'u', password: 'bad'),
        throwsA(isA<AppFailure>()),
      );

      expect(api.history.any((r) => r.path.endsWith('/reveal')), isFalse);
    });

    test('31 reveal counters are not incremented locally', () async {
      final source = await _source('lib/runtime/app_runtime.dart');
      expect(source, isNot(contains('remainingReveals++')));
      expect(source, isNot(contains('remainingReveals + 1')));
      expect(source, contains('message.remainingReveals?.toString()'));
    });

    test(
      '32 secure authentication alone does not mark message opened',
      () async {
        await storage.writeSession(_session());
        api.enqueue('POST', '/api/mobile/inbox/99/authenticate', 200, {
          'revealToken': 'grant-1',
          'revealExpiresAtUtc': '2035-01-01T00:00:00Z',
        });

        final grant = await inbox.authenticate(
          deliveryId: 99,
          username: 'u',
          password: 'p',
        );

        expect(grant.revealToken, 'grant-1');
        expect(api.history, hasLength(1));
      },
    );

    test(
      '33 successful reveal returns protected sanitized message body',
      () async {
        await storage.writeSession(_session());
        api.enqueue('POST', '/api/mobile/inbox/99/reveal', 200, _revealJson());

        final message = await inbox.reveal(
          deliveryId: 99,
          revealToken: 'grant-1',
        );

        expect(message.contentEnglishHtml, '<p>Secure body</p>');
      },
    );

    test(
      '34 reveal state uses authoritative server remaining counter',
      () async {
        await storage.writeSession(_session());
        api.enqueue(
          'POST',
          '/api/mobile/inbox/99/reveal',
          200,
          _revealJson(remaining: 2),
        );

        final message = await inbox.reveal(
          deliveryId: 99,
          revealToken: 'grant-1',
        );

        expect(message.remainingReveals, 2);
      },
    );

    test('35 text with zero attachments is a valid reveal response', () async {
      await storage.writeSession(_session());
      api.enqueue('POST', '/api/mobile/inbox/99/reveal', 200, _revealJson());

      final message = await inbox.reveal(
        deliveryId: 99,
        revealToken: 'grant-1',
      );

      expect(message.attachments, isEmpty);
      expect(message.contentEnglishHtml, isNotEmpty);
    });

    test(
      '36 access-limit exhaustion blocks reveal through failure mapping',
      () async {
        await storage.writeSession(_session());
        api.enqueue(
          'POST',
          '/api/mobile/inbox/99/reveal',
          410,
          _errorJson('REVEAL_LIMIT_REACHED'),
        );

        await expectLater(
          inbox.reveal(deliveryId: 99, revealToken: 'grant-1'),
          throwsA(
            isA<AppFailure>().having(
              (f) => f.kind,
              'kind',
              AppFailureKind.accessLimit,
            ),
          ),
        );
      },
    );
  });

  group('push routing contracts', () {
    test('37 authenticated push tap routes only to secure login', () {
      final state = AppNavigationState()..completeAuthentication();
      expect(state.destinationForPush('99'), '/delivery/99/login');
    });

    test('38 unauthenticated push preserves pending delivery and goes to mobile auth', () {
      final state = AppNavigationState();
      expect(state.destinationForPush('99'), '/auth/mobile');
      expect(state.pendingDeliveryId, '99');
    });

    test('39 auth completion resumes pending delivery at secure login', () {
      final state = AppNavigationState()..rememberPendingDelivery('99');
      state.completeAuthentication();
      expect(state.postAuthenticationDestination(), '/delivery/99/login');
    });

    test(
      '40 push payload without deliveryId is rejected by coordinator',
      () async {
        final source = await _source(
          'lib/firebase/firebase_messaging_coordinator.dart',
        );
        expect(source, contains("data['deliveryId']"));
        expect(source, contains('if (id == null || id <= 0) return null;'));
      },
    );

    test('41 malformed or non-positive deliveryId is ignored safely', () async {
      final source = await _source(
        'lib/firebase/firebase_messaging_coordinator.dart',
      );
      expect(
        source,
        contains("int.tryParse(data['deliveryId']?.toString() ?? '')"),
      );
      expect(source, contains('return id.toString();'));
    });

    test(
      '42 push tap callback never receives or reveals protected body',
      () async {
        final source = await _source(
          'lib/firebase/firebase_messaging_coordinator.dart',
        );
        expect(source, contains('await onDeliveryOpened(deliveryId);'));
        expect(source, isNot(contains('revealToken')));
        expect(source, isNot(contains('contentEnglishHtml')));
      },
    );

    test('43 foreground push refreshes safe UI without exposing secure body metadata', () async {
      final source = await _source(
        'lib/firebase/firebase_messaging_coordinator.dart',
      );
      expect(source, contains('await onForegroundDelivery();'));
      expect(
        source,
        contains("data['notificationCategory'] != 'secure_delivery'"),
      );
      expect(source, isNot(contains("data['content']")));
    });
  });

  AppRuntime createRuntime() => AppRuntime(
    auth: auth,
    inbox: inbox,
    storage: storage,
    biometrics: BiometricService(),
    client: client,
  );
}

class _QueueApiInterceptor extends Interceptor {
  final Map<String, List<_QueuedReply>> _replies = {};
  final List<RequestOptions> history = [];

  void enqueue(String method, String path, int statusCode, dynamic data) {
    final key = '${method.toUpperCase()} $path';
    _replies.putIfAbsent(key, () => []).add(_QueuedReply(statusCode, data));
  }

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    history.add(options);
    final key = '${options.method.toUpperCase()} ${options.path}';
    final queue = _replies[key];
    if (queue == null || queue.isEmpty) {
      handler.reject(
        DioException(
          requestOptions: options,
          error: StateError('No queued response for $key'),
          type: DioExceptionType.unknown,
        ),
      );
      return;
    }

    final reply = queue.removeAt(0);
    final response = Response<dynamic>(
      requestOptions: options,
      statusCode: reply.statusCode,
      data: reply.data,
    );
    if (reply.statusCode >= 200 && reply.statusCode < 300) {
      handler.resolve(response);
      return;
    }

    handler.reject(
      DioException(
        requestOptions: options,
        response: response,
        type: DioExceptionType.badResponse,
      ),
    );
  }
}

class _QueuedReply {
  const _QueuedReply(this.statusCode, this.data);

  final int statusCode;
  final dynamic data;
}

MobileSession _session({bool refreshExpired = false}) => MobileSession(
  accessToken: 'access-1',
  accessExpiresAtUtc: DateTime.utc(2035, 1, 1),
  refreshToken: 'refresh-1',
  refreshExpiresAtUtc: refreshExpired
      ? DateTime.utc(2020, 1, 1)
      : DateTime.utc(2035, 2, 1),
  sessionId: 'session-1',
  organization: const OrganizationProfile(
    id: 7,
    nameArabic: 'جهة الخادم',
    nameEnglish: 'Server Organization',
  ),
);

Map<String, dynamic> _sessionJson({
  String access = 'access-1',
  String refresh = 'refresh-1',
}) => {
  'code': 'AUTHENTICATED',
  'accessToken': access,
  'accessExpiresAtUtc': '2035-01-01T00:00:00Z',
  'refreshToken': refresh,
  'refreshExpiresAtUtc': '2035-02-01T00:00:00Z',
  'sessionId': 'session-1',
  'organization': {
    'id': 7,
    'nameArabic': 'جهة الخادم',
    'nameEnglish': 'Server Organization',
  },
};

Map<String, dynamic> _challengeJson() => {
  'code': 'OTP_REQUEST_ACCEPTED',
  'challengeId': 'challenge-1',
  'expiresAtUtc': '2035-01-01T00:05:00Z',
  'resendAvailableAtUtc': '2035-01-01T00:01:00Z',
};

Map<String, dynamic> _meJson() => {
  'organization': {
    'id': 7,
    'nameArabic': 'جهة الخادم',
    'nameEnglish': 'Server Organization',
  },
  'session': {
    'sessionId': 'session-1',
    'accessExpiresAtUtc': '2035-01-01T00:00:00Z',
    'refreshExpiresAtUtc': '2035-02-01T00:00:00Z',
  },
  'registeredDeviceCount': 1,
};

Map<String, dynamic> _emptyInboxJson() => {
  'headingArabic': 'لديك رسالة جديدة اضغط هنا لاستعراض الرسالة',
  'headingEnglish': 'You have a new message. Tap here to view it.',
  'page': 1,
  'pageSize': 20,
  'totalCount': 0,
  'items': <dynamic>[],
};

Map<String, dynamic> _inboxJson() => {
  ..._emptyInboxJson(),
  'totalCount': 1,
  'items': [
    {
      'deliveryId': 99,
      'sentAtUtc': '2030-01-01T00:00:00Z',
      'expiresAtUtc': '2035-01-01T00:00:00Z',
      'firstRevealedAtUtc': null,
      'remainingReveals': 3,
      'status': 'SUCCESS',
    },
  ],
};

Map<String, dynamic> _revealJson({int remaining = 3}) => {
  'code': 'SECURE_MESSAGE_REVEALED',
  'headingArabic': 'لديك رسالة جديدة اضغط هنا لاستعراض الرسالة',
  'headingEnglish': 'You have a new message. Tap here to view it.',
  'contentArabicHtml': '<p>محتوى آمن</p>',
  'contentEnglishHtml': '<p>Secure body</p>',
  'sentAtUtc': '2030-01-01T00:00:00Z',
  'expiresAtUtc': '2035-01-01T00:00:00Z',
  'remainingReveals': remaining,
  'firstRevealedAtUtc': '2030-01-01T00:01:00Z',
  'attachments': <dynamic>[],
};

Map<String, dynamic> _errorJson(String code) => {
  'error': {
    'code': code,
    'messageArabic': 'تعذر إتمام الطلب.',
    'messageEnglish': 'The request could not be completed.',
  },
};

Future<String> _source(String relativePath) async =>
    File(relativePath).readAsString();
