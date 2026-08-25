abstract final class AppConfig {
  static const appName = 'DA Secure';
  static const androidPackage = 'com.qr.mobile.da';
  static const apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'https://testapi.da.gov.kw',
  );
}
