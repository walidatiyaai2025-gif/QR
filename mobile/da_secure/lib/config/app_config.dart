abstract final class AppConfig {
  static const appName = 'DA Secure';
  static const androidPackage = 'com.qr.mobile.da';
  static const appVersion = '0.1.0';
  static const apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'https://testapi.da.gov.kw',
  );
}
