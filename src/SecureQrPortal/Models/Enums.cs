namespace SecureQrPortal.Models;

public enum AccessLimitMode
{
    Unlimited = 0,
    ExpiryDateOnly = 1,
    MaximumSuccessfulAccesses = 2,
    MaximumQrOpens = 3,
    ExpiryAndSuccessfulAccesses = 4,
    ExpiryAndQrOpens = 5
}

public enum QrStatus
{
    ACTIVE,
    NOT_STARTED,
    EXPIRED,
    DISABLED,
    REVOKED,
    LIMIT_REACHED
}

public enum AccessEventType
{
    QR_OPEN,
    LOGIN_SUCCESS,
    LOGIN_FAILURE,
    PAGE_VIEW,
    TOKEN_INVALID,
    TOKEN_EXPIRED,
    TOKEN_REVOKED,
    LIMIT_REACHED,
    TOKEN_NOT_STARTED,
    TOKEN_DISABLED
}
