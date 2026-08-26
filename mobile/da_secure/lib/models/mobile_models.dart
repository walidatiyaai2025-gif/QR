class OrganizationProfile {
  const OrganizationProfile({
    required this.id,
    required this.nameArabic,
    required this.nameEnglish,
  });

  final int id;
  final String nameArabic;
  final String nameEnglish;

  factory OrganizationProfile.fromJson(Map<String, dynamic> json) {
    return OrganizationProfile(
      id: _requiredInt(json['id'], 'organization.id', minimum: 1),
      nameArabic: (json['nameArabic'] as String?)?.trim() ?? '',
      nameEnglish: (json['nameEnglish'] as String?)?.trim() ?? '',
    );
  }

  Map<String, dynamic> toJson() => {
    'id': id,
    'nameArabic': nameArabic,
    'nameEnglish': nameEnglish,
  };

  String displayName(bool arabic) {
    final preferred = arabic ? nameArabic : nameEnglish;
    final fallback = arabic ? nameEnglish : nameArabic;
    return preferred.isNotEmpty ? preferred : fallback;
  }
}

class MobileSession {
  const MobileSession({
    required this.accessToken,
    required this.accessExpiresAtUtc,
    required this.refreshToken,
    required this.refreshExpiresAtUtc,
    required this.sessionId,
    required this.organization,
  });

  final String accessToken;
  final DateTime accessExpiresAtUtc;
  final String refreshToken;
  final DateTime refreshExpiresAtUtc;
  final String sessionId;
  final OrganizationProfile organization;

  factory MobileSession.fromJson(Map<String, dynamic> json) {
    final organizationJson = _asMap(json['organization']);
    return MobileSession(
      accessToken: _requiredString(json['accessToken'], 'accessToken'),
      accessExpiresAtUtc: _requiredDate(
        json['accessExpiresAtUtc'],
        'accessExpiresAtUtc',
      ),
      refreshToken: _requiredString(json['refreshToken'], 'refreshToken'),
      refreshExpiresAtUtc: _requiredDate(
        json['refreshExpiresAtUtc'],
        'refreshExpiresAtUtc',
      ),
      sessionId: _requiredString(json['sessionId'], 'sessionId'),
      organization: OrganizationProfile.fromJson(organizationJson),
    );
  }

  Map<String, dynamic> toJson() => {
    'accessToken': accessToken,
    'accessExpiresAtUtc': accessExpiresAtUtc.toUtc().toIso8601String(),
    'refreshToken': refreshToken,
    'refreshExpiresAtUtc': refreshExpiresAtUtc.toUtc().toIso8601String(),
    'sessionId': sessionId,
    'organization': organization.toJson(),
  };

  bool get refreshExpired =>
      !refreshExpiresAtUtc.toUtc().isAfter(DateTime.now().toUtc());
}

class OtpChallenge {
  const OtpChallenge({
    required this.challengeId,
    required this.expiresAtUtc,
    required this.resendAvailableAtUtc,
  });

  final String challengeId;
  final DateTime expiresAtUtc;
  final DateTime resendAvailableAtUtc;

  factory OtpChallenge.fromJson(Map<String, dynamic> json) => OtpChallenge(
    challengeId: _requiredString(json['challengeId'], 'challengeId'),
    expiresAtUtc: _requiredDate(json['expiresAtUtc'], 'expiresAtUtc'),
    resendAvailableAtUtc: _requiredDate(
      json['resendAvailableAtUtc'],
      'resendAvailableAtUtc',
    ),
  );
}

class CurrentUser {
  const CurrentUser({
    required this.organization,
    required this.sessionId,
    required this.accessExpiresAtUtc,
    required this.refreshExpiresAtUtc,
    required this.registeredDeviceCount,
  });

  final OrganizationProfile organization;
  final String sessionId;
  final DateTime accessExpiresAtUtc;
  final DateTime refreshExpiresAtUtc;
  final int registeredDeviceCount;

  factory CurrentUser.fromJson(Map<String, dynamic> json) {
    final session = _asMap(json['session']);
    return CurrentUser(
      organization: OrganizationProfile.fromJson(_asMap(json['organization'])),
      sessionId: _requiredString(session['sessionId'], 'session.sessionId'),
      accessExpiresAtUtc: _requiredDate(
        session['accessExpiresAtUtc'],
        'session.accessExpiresAtUtc',
      ),
      refreshExpiresAtUtc: _requiredDate(
        session['refreshExpiresAtUtc'],
        'session.refreshExpiresAtUtc',
      ),
      registeredDeviceCount: _requiredInt(
        json['registeredDeviceCount'],
        'registeredDeviceCount',
        minimum: 0,
      ),
    );
  }
}

class InboxItem {
  const InboxItem({
    required this.deliveryId,
    required this.status,
    this.sentAtUtc,
    this.expiresAtUtc,
    this.firstRevealedAtUtc,
    this.remainingReveals,
  });

  final int deliveryId;
  final DateTime? sentAtUtc;
  final DateTime? expiresAtUtc;
  final DateTime? firstRevealedAtUtc;
  final int? remainingReveals;
  final String status;

  factory InboxItem.fromJson(Map<String, dynamic> json) => InboxItem(
    deliveryId: _requiredInt(json['deliveryId'], 'deliveryId', minimum: 1),
    sentAtUtc: _optionalDate(json['sentAtUtc'], 'sentAtUtc'),
    expiresAtUtc: _optionalDate(json['expiresAtUtc'], 'expiresAtUtc'),
    firstRevealedAtUtc: _optionalDate(
      json['firstRevealedAtUtc'],
      'firstRevealedAtUtc',
    ),
    remainingReveals: _optionalInt(
      json['remainingReveals'],
      'remainingReveals',
      minimum: 0,
    ),
    status: _requiredString(json['status'], 'status').toUpperCase(),
  );
}

class InboxPage {
  const InboxPage({
    required this.headingArabic,
    required this.headingEnglish,
    required this.page,
    required this.pageSize,
    required this.totalCount,
    required this.items,
  });

  final String headingArabic;
  final String headingEnglish;
  final int page;
  final int pageSize;
  final int totalCount;
  final List<InboxItem> items;

  factory InboxPage.fromJson(Map<String, dynamic> json) {
    final value = json['items'];
    if (value is! List) throw const FormatException('Invalid items.');
    return InboxPage(
      headingArabic: (json['headingArabic'] as String?)?.trim() ?? '',
      headingEnglish: (json['headingEnglish'] as String?)?.trim() ?? '',
      page: _requiredInt(json['page'], 'page', minimum: 1),
      pageSize: _requiredInt(json['pageSize'], 'pageSize', minimum: 1),
      totalCount: _requiredInt(json['totalCount'], 'totalCount', minimum: 0),
      items: value.map((item) => InboxItem.fromJson(_asMap(item))).toList(),
    );
  }
}

class DeliveryDetails {
  const DeliveryDetails({
    required this.deliveryId,
    required this.status,
    this.sentAtUtc,
    this.expiresAtUtc,
    this.firstRevealedAtUtc,
    this.remainingReveals,
  });

  final int deliveryId;
  final DateTime? sentAtUtc;
  final DateTime? expiresAtUtc;
  final DateTime? firstRevealedAtUtc;
  final int? remainingReveals;
  final String status;

  factory DeliveryDetails.fromJson(Map<String, dynamic> json) =>
      DeliveryDetails(
        deliveryId: _requiredInt(json['deliveryId'], 'deliveryId', minimum: 1),
        sentAtUtc: _optionalDate(json['sentAtUtc'], 'sentAtUtc'),
        expiresAtUtc: _optionalDate(json['expiresAtUtc'], 'expiresAtUtc'),
        firstRevealedAtUtc: _optionalDate(
          json['firstRevealedAtUtc'],
          'firstRevealedAtUtc',
        ),
        remainingReveals: _optionalInt(
          json['remainingReveals'],
          'remainingReveals',
          minimum: 0,
        ),
        status: _requiredString(json['status'], 'status').toUpperCase(),
      );
}

class RevealGrant {
  const RevealGrant({required this.revealToken, required this.expiresAtUtc});

  final String revealToken;
  final DateTime expiresAtUtc;

  factory RevealGrant.fromJson(Map<String, dynamic> json) => RevealGrant(
    revealToken: _requiredString(json['revealToken'], 'revealToken'),
    expiresAtUtc: _requiredDate(
      json['revealExpiresAtUtc'],
      'revealExpiresAtUtc',
    ),
  );
}

class SecureMessage {
  const SecureMessage({
    required this.headingArabic,
    required this.headingEnglish,
    required this.contentArabicHtml,
    required this.contentEnglishHtml,
    required this.attachments,
    this.sentAtUtc,
    this.expiresAtUtc,
    this.remainingReveals,
    this.firstRevealedAtUtc,
  });

  final String headingArabic;
  final String headingEnglish;
  final String contentArabicHtml;
  final String contentEnglishHtml;
  final DateTime? sentAtUtc;
  final DateTime? expiresAtUtc;
  final int? remainingReveals;
  final DateTime? firstRevealedAtUtc;
  final List<Map<String, dynamic>> attachments;

  factory SecureMessage.fromJson(Map<String, dynamic> json) {
    final rawAttachments = json['attachments'];
    if (rawAttachments is! List) {
      throw const FormatException('Invalid attachments.');
    }
    return SecureMessage(
      headingArabic: (json['headingArabic'] as String?)?.trim() ?? '',
      headingEnglish: (json['headingEnglish'] as String?)?.trim() ?? '',
      contentArabicHtml: (json['contentArabicHtml'] as String?) ?? '',
      contentEnglishHtml: (json['contentEnglishHtml'] as String?) ?? '',
      sentAtUtc: _optionalDate(json['sentAtUtc'], 'sentAtUtc'),
      expiresAtUtc: _optionalDate(json['expiresAtUtc'], 'expiresAtUtc'),
      remainingReveals: _optionalInt(
        json['remainingReveals'],
        'remainingReveals',
        minimum: 0,
      ),
      firstRevealedAtUtc: _optionalDate(
        json['firstRevealedAtUtc'],
        'firstRevealedAtUtc',
      ),
      attachments: rawAttachments.map(_asMap).toList(),
    );
  }

  String contentFor(bool arabic) {
    final preferred = arabic ? contentArabicHtml : contentEnglishHtml;
    final fallback = arabic ? contentEnglishHtml : contentArabicHtml;
    return preferred.isNotEmpty ? preferred : fallback;
  }
}

Map<String, dynamic> _asMap(dynamic value) {
  if (value is Map<String, dynamic>) return value;
  if (value is Map) {
    return value.map((key, item) => MapEntry(key.toString(), item));
  }
  throw const FormatException('Expected JSON object.');
}

String _requiredString(dynamic value, String field) {
  final result = value is String ? value.trim() : '';
  if (result.isEmpty) throw FormatException('Missing $field.');
  return result;
}

DateTime _requiredDate(dynamic value, String field) {
  final parsed = _optionalDate(value, field);
  if (parsed == null) throw FormatException('Missing $field.');
  return parsed;
}

DateTime? _optionalDate(dynamic value, String field) {
  if (value == null) return null;
  if (value is! String || value.trim().isEmpty) {
    throw FormatException('Invalid $field.');
  }
  final parsed = DateTime.tryParse(value);
  if (parsed == null) throw FormatException('Invalid $field.');
  return parsed.toUtc();
}

int _requiredInt(dynamic value, String field, {required int minimum}) {
  final parsed = _parseInt(value);
  if (parsed == null || parsed < minimum) {
    throw FormatException('Invalid $field.');
  }
  return parsed;
}

int? _optionalInt(dynamic value, String field, {required int minimum}) {
  if (value == null) return null;
  final parsed = _parseInt(value);
  if (parsed == null || parsed < minimum) {
    throw FormatException('Invalid $field.');
  }
  return parsed;
}

int? _parseInt(dynamic value) {
  if (value is int) return value;
  if (value is num && value.isFinite && value == value.truncateToDouble()) {
    return value.toInt();
  }
  if (value is String && RegExp(r'^-?\d+$').hasMatch(value.trim())) {
    return int.tryParse(value.trim());
  }
  return null;
}
