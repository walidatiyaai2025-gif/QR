import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:flutter/material.dart';

class MobileNumberScreen extends StatelessWidget {
  const MobileNumberScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(backgroundColor: Colors.transparent),
        body: ListView(
          padding: const EdgeInsets.fromLTRB(24, 24, 24, 32),
          children: [
            const Center(child: Icon(Icons.account_balance, size: 64, color: DaSecureColors.gold)),
            const SizedBox(height: 24),
            const Text('تسجيل الدخول', textAlign: TextAlign.center, style: TextStyle(fontSize: 28, fontWeight: FontWeight.w700)),
            const SizedBox(height: 8),
            const Text('أدخل رقم الجوال المسجل لتسجيل الدخول', textAlign: TextAlign.center, style: TextStyle(color: DaSecureColors.textMuted)),
            const SizedBox(height: 32),
            const Text('رقم الجوال'),
            const SizedBox(height: 8),
            const TextField(enabled: false, textDirection: TextDirection.ltr, decoration: InputDecoration(hintText: '+965 5555 1234')),
            const SizedBox(height: 16),
            FilledButton(onPressed: null, child: const Text('طلب رمز التحقق')),
            const SizedBox(height: 16),
            const Text('اتصال OTP الحقيقي لم يُربط بعد؛ لا توجد بيانات إنتاج وهمية.', textAlign: TextAlign.center, style: TextStyle(color: DaSecureColors.textMuted, fontSize: 12)),
          ],
        ),
      ),
    );
  }
}
