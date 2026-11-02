namespace ViettelConnect.MySign.Domain.Certificates;

/// <summary>Chứng thư số của người ký (kết quả certificates/info).</summary>
public sealed record Certificate(
    string CredentialId,
    string? SerialNumber,
    string? SubjectDn,
    string? IssuerDn,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool KeyEnabled,
    IReadOnlyList<string> SignAlgorithmOids,
    IReadOnlyList<byte[]> Chain /* DER, chứng thư người ký đứng đầu */);
