using ViettelConnect.MySign.Domain.Certificates;

namespace ViettelConnect.MySign.Infrastructure.Wire.Mapping;

internal static class CertificateMapper
{
    /// <summary>Ném <see cref="FormatException"/> nếu thiếu credential_id hoặc chuỗi base64 hỏng; client đổi thành ProtocolViolation.</summary>
    public static Certificate ToDomain(CertificateInfoDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.CredentialId))
        {
            throw new FormatException("credential_id is missing.");
        }

        var chain = (dto.Cert?.Certificates ?? []).Select(Base64.Decode).ToArray();

        return new Certificate(
            CredentialId: dto.CredentialId,
            SerialNumber: dto.Cert?.SerialNumber,
            SubjectDn: dto.Cert?.SubjectDn,
            IssuerDn: dto.Cert?.IssuerDn,
            ValidFrom: GeneralizedTime.TryParse(dto.Cert?.ValidFrom),
            ValidTo: GeneralizedTime.TryParse(dto.Cert?.ValidTo),
            KeyEnabled: string.Equals(dto.Key?.Status, "ENABLED", StringComparison.OrdinalIgnoreCase),
            SignAlgorithmOids: dto.Key?.Algo?.ToArray() ?? [],
            Chain: chain);
    }
}
