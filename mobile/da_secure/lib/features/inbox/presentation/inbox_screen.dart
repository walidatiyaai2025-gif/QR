import 'package:da_secure/design_system/da_secure_theme.dart';
import 'package:flutter/material.dart';

class InboxScreen extends StatelessWidget {
  const InboxScreen({super.key});
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
              Text('الوارد', style: TextStyle(fontSize: 30, fontWeight: FontWeight.w700)),
              SizedBox(height: 8),
              Text('لا توجد رسائل محملة. يجب أن تأتي البطاقات من API الحقيقي للمؤسسة المصادق عليها.', style: TextStyle(color: DaSecureColors.textMuted)),
            ],
          ),
        ),
      ),
    ),
  );
}
