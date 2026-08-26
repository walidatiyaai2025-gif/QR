import 'dart:io';

import 'package:da_secure/config/app_config.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test(
    'final QA pins the mobile runtime to the canonical verified HTTPS API',
    () {
      final uri = Uri.parse(AppConfig.apiBaseUrl);

      expect(uri.scheme, 'https');
      expect(uri.host, 'testapi.da.gov.kw');
      expect(uri.userInfo, isEmpty);
      expect(uri.fragment, isEmpty);
      expect(AppConfig.apiBaseUrl, 'https://testapi.da.gov.kw');
    },
  );

  test(
    'mobile production sources contain no HTTP fallback or trust-all TLS hook',
    () {
      final sourceFiles = Directory('lib')
          .listSync(recursive: true)
          .whereType<File>()
          .where((file) => file.path.endsWith('.dart'));

      final forbidden = <String>[
        'http://testapi.da.gov.kw',
        'badCertificateCallback',
        'HttpOverrides.global',
        'allowBadCertificates',
        'dangerousAcceptAnyServerCertificateValidator',
        'skipCertificateCheck',
      ];

      for (final file in sourceFiles) {
        final source = file.readAsStringSync();
        for (final marker in forbidden) {
          expect(
            source.toLowerCase(),
            isNot(contains(marker.toLowerCase())),
            reason:
                'Forbidden TLS/HTTP bypass marker "$marker" found in ${file.path}',
          );
        }
      }
    },
  );
}
