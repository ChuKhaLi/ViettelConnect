namespace ViettelConnect.MySign.Domain.Certificates;

/// <summary>
/// Chứng thư số của người ký (kết quả certificates/info). Các OID thuật toán ký là chi tiết wire nên chỉ đọc được
/// nội bộ (<see cref="SignAlgorithmOids"/> là <c>internal</c>); ứng dụng chỉ thấy các thuộc tính còn lại.
/// </summary>
public sealed record Certificate
{
    /// <summary>Tạo chứng thư không kèm thông tin thuật toán ký (dùng khi ứng dụng tự dựng, vd trong test).</summary>
    public Certificate(
        string credentialId,
        string? serialNumber,
        string? subjectDn,
        string? issuerDn,
        DateTimeOffset? validFrom,
        DateTimeOffset? validTo,
        bool keyEnabled,
        IReadOnlyList<byte[]> chain /* DER, chứng thư người ký đứng đầu */)
        : this(credentialId, serialNumber, subjectDn, issuerDn, validFrom, validTo, keyEnabled, [], chain)
    {
    }

    internal Certificate(
        string credentialId,
        string? serialNumber,
        string? subjectDn,
        string? issuerDn,
        DateTimeOffset? validFrom,
        DateTimeOffset? validTo,
        bool keyEnabled,
        IReadOnlyList<string> signAlgorithmOids,
        IReadOnlyList<byte[]> chain)
    {
        CredentialId = credentialId;
        SerialNumber = serialNumber;
        SubjectDn = subjectDn;
        IssuerDn = issuerDn;
        ValidFrom = validFrom;
        ValidTo = validTo;
        KeyEnabled = keyEnabled;
        SignAlgorithmOids = signAlgorithmOids;
        Chain = chain;
    }

    public string CredentialId { get; init; }

    public string? SerialNumber { get; init; }

    public string? SubjectDn { get; init; }

    public string? IssuerDn { get; init; }

    public DateTimeOffset? ValidFrom { get; init; }

    public DateTimeOffset? ValidTo { get; init; }

    public bool KeyEnabled { get; init; }

    /// <summary>OID thuật toán ký khoá hỗ trợ (chi tiết wire, không công khai).</summary>
    internal IReadOnlyList<string> SignAlgorithmOids { get; init; }

    /// <summary>Chuỗi chứng thư DER, chứng thư người ký đứng đầu.</summary>
    public IReadOnlyList<byte[]> Chain { get; init; }
}
