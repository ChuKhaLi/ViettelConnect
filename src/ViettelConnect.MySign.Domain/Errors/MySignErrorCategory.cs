namespace ViettelConnect.MySign.Domain.Errors;

public enum MySignErrorCategory
{
    Unknown,
    Authentication,
    Validation,
    NoActiveCertificate,
    SignRejected,
    SignTimeout,
    SignFailed,
    InsufficientQuota,
    CertificateInvalid,
    Transport,
    ProtocolViolation,
    CallbackValidation,
}
