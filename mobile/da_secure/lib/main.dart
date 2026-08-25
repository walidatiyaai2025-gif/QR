import 'package:da_secure/config/app_config.dart';
import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/firebase/firebase_bootstrap.dart';
import 'package:da_secure/routing/app_router.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await FirebaseBootstrap.initializeClient();
  runApp(const DaSecureApp());
}

class DaSecureApp extends StatelessWidget {
  const DaSecureApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp.router(
      debugShowCheckedModeBanner: false,
      title: AppConfig.appName,
      theme: DaSecureTheme.light,
      supportedLocales: const [
        Locale('ar'),
        Locale('en'),
      ],
      localizationsDelegates: GlobalMaterialLocalizations.delegates,
      localeResolutionCallback: (deviceLocale, supportedLocales) {
        if (deviceLocale?.languageCode == 'en') {
          return const Locale('en');
        }

        return const Locale('ar');
      },
      routerConfig: appRouter,
    );
  }
}
