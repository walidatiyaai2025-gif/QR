import 'package:da_secure/config/app_config.dart';
import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:da_secure/firebase/firebase_bootstrap.dart';
import 'package:da_secure/routing/app_router.dart';
import 'package:flutter/material.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await FirebaseBootstrap.initializeClientIfConfigured();
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
      routerConfig: appRouter,
    );
  }
}
