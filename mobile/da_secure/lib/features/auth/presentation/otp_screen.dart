import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:flutter/material.dart';

class OtpScreen extends StatelessWidget {
  const OtpScreen({super.key});
  @override
  Widget build(BuildContext context) => const Directionality(
    textDirection: TextDirection.rtl,
    child: Scaffold(
      body: SafeArea(
        child: Padding(
          padding: EdgeInsets.all(24),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text('تحقق من الرمز', style: TextStyle(fontSize: 28, fontWeight: FontWeight.w700)),
              SizedBox(height: 8),
              Text('شاشة مرجعية فقط حتى يتم ربط OTP الحقيقي.', style: TextStyle(color: DaSecureColors.textMuted)),
            ],
          ),
        ),
      ),
    ),
  );
}
