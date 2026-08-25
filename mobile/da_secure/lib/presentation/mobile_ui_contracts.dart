enum UiPhase {
  idle,
  loading,
  empty,
  success,
  error,
}

class MobileNumberUiState {
  const MobileNumberUiState({
    this.isSubmitting = false,
    this.errorMessage,
  });

  final bool isSubmitting;
  final String? errorMessage;
}

class OtpUiState {
  const OtpUiState({
    this.isSubmitting = false,
    this.resendSeconds = 0,
    this.errorMessage,
  });

  final bool isSubmitting;
  final int resendSeconds;
  final String? errorMessage;
}

class BiometricUiState {
  const BiometricUiState({
    this.isBusy = false,
    this.errorMessage,
  });

  final bool isBusy;
  final String? errorMessage;
}

class InboxDeliveryUiModel {
  const InboxDeliveryUiModel({
    required this.deliveryId,
    this.sentLabel,
    this.expiryLabel,
    this.remainingRevealsLabel,
    this.status,
  });

  final String deliveryId;
  final String? sentLabel;
  final String? expiryLabel;
  final String? remainingRevealsLabel;
  final String? status;
}

class InboxUiState {
  const InboxUiState({
    this.phase = UiPhase.empty,
    this.organizationName,
    this.items = const <InboxDeliveryUiModel>[],
    this.errorMessage,
  });

  final UiPhase phase;
  final String? organizationName;
  final List<InboxDeliveryUiModel> items;
  final String? errorMessage;
}

enum SecureDeliveryUiPhase {
  loading,
  ready,
  submitting,
  success,
  expired,
  revoked,
  limitReached,
  authenticationFailure,
  error,
}

class SecureLoginUiState {
  const SecureLoginUiState({
    this.phase = SecureDeliveryUiPhase.ready,
    this.organizationName,
    this.errorMessage,
  });

  final SecureDeliveryUiPhase phase;
  final String? organizationName;
  final String? errorMessage;
}

class AttachmentUiModel {
  const AttachmentUiModel({
    required this.name,
    this.sizeLabel,
  });

  final String name;
  final String? sizeLabel;
}

class SecureMessageUiState {
  const SecureMessageUiState({
    this.phase = SecureDeliveryUiPhase.loading,
    this.organizationName,
    this.bodyText,
    this.bodyHtml,
    this.remainingRevealsLabel,
    this.expiryLabel,
    this.attachments = const <AttachmentUiModel>[],
    this.errorMessage,
  });

  final SecureDeliveryUiPhase phase;
  final String? organizationName;
  final String? bodyText;
  final String? bodyHtml;
  final String? remainingRevealsLabel;
  final String? expiryLabel;
  final List<AttachmentUiModel> attachments;
  final String? errorMessage;
}
