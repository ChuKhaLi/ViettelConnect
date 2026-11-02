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

    /// <summary>
    /// Dự phòng, hiện không được SDK ném: callback không hợp lệ được báo bằng <c>status_code</c> khác <c>"00"</c>
    /// trong phản hồi callback, không bằng ngoại lệ.
    /// </summary>
    CallbackValidation,
}
