import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:flutter/material.dart';

class BiometricScreen extends StatelessWidget {
  const BiometricScreen({super.key});
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
              Icon(Icons.face_retouching_natural, size: 72, color: DaSecureColors.gold),
              SizedBox(height: 20),
              Text('تفعيل بصمة الجهاز؟', textAlign: TextAlign.center, style: TextStyle(fontSize: 26, fontWeight: FontWeight.w700)),
              SizedBox(height: 8),
              Text('البصمة اختيارية ولا تستبدل بيانات اعتماد الرسالة الآمنة.', textAlign: TextAlign.center, style: TextStyle(color: DaSecureColors.textMuted)),
            ],
          ),
        ),
      ),
    ),
  );
}
