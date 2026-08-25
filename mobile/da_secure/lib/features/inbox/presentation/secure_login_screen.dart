import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:flutter/material.dart';

class SecureLoginScreen extends StatelessWidget {
  const SecureLoginScreen({required this.deliveryId, super.key});
  final String deliveryId;

  @override
  Widget build(BuildContext context) => Directionality(
    textDirection: TextDirection.rtl,
    child: Scaffold(
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Text('تسجيل الدخول', style: TextStyle(fontSize: 28, fontWeight: FontWeight.w700)),
              const SizedBox(height: 8),
              Text('Delivery: $deliveryId', style: const TextStyle(color: DaSecureColors.textMuted, fontSize: 11)),
              const SizedBox(height: 12),
              const Text('يجب استخدام نفس اسم المستخدم وكلمة المرور الخاصة بالصفحة الآمنة. لم يتم ربط الاعتماد الحقيقي بعد.', style: TextStyle(color: DaSecureColors.textMuted)),
            ],
          ),
        ),
      ),
    ),
  );
}
