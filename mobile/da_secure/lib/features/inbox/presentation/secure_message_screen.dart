import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:flutter/material.dart';

class SecureMessageScreen extends StatelessWidget {
  const SecureMessageScreen({required this.deliveryId, super.key});
  final String deliveryId;

  @override
  Widget build(BuildContext context) => Directionality(
    textDirection: TextDirection.rtl,
    child: Scaffold(
      appBar: AppBar(title: const Text('الرسالة الآمنة')),
      body: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const Text('لديك رسالة جديدة اضغط هنا لاستعراض الرسالة', style: TextStyle(fontSize: 21, fontWeight: FontWeight.w700)),
            const SizedBox(height: 12),
            Text('Delivery: $deliveryId', style: const TextStyle(color: DaSecureColors.textMuted, fontSize: 11)),
            const SizedBox(height: 20),
            const Text('لم يتم كشف محتوى وهمي. يجب عرض النص المعقم القادم من الخادم بعد المصادقة الآمنة الناجحة فقط.', style: TextStyle(color: DaSecureColors.textMuted)),
          ],
        ),
      ),
    ),
  );
}
