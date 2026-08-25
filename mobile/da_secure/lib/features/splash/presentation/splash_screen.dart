import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

class SplashScreen extends StatefulWidget {
  const SplashScreen({super.key});

  @override
  State<SplashScreen> createState() => _SplashScreenState();
}

class _SplashScreenState extends State<SplashScreen> {
  @override
  void initState() {
    super.initState();
    Future<void>.delayed(const Duration(milliseconds: 900), () {
      if (mounted) context.go('/auth/mobile');
    });
  }

  @override
  Widget build(BuildContext context) {
    return const Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        body: Center(
          child: Padding(
            padding: EdgeInsets.all(32),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(Icons.account_balance, size: 92, color: DaSecureColors.gold),
                SizedBox(height: 24),
                Text('الديوان الأميري', style: TextStyle(fontSize: 30, fontWeight: FontWeight.w700)),
                SizedBox(height: 8),
                Text('DA Secure', style: TextStyle(color: DaSecureColors.goldSoft, fontSize: 18)),
                SizedBox(height: 12),
                Text('الهوية الرسمية ستستخدم شعار الديوان الأميري المعتمد', textAlign: TextAlign.center, style: TextStyle(color: DaSecureColors.textMuted, fontSize: 12)),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
