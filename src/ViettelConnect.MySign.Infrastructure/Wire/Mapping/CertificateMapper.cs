using ViettelConnect.MySign.Application.Signing;
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
            credentialId: dto.CredentialId,
            serialNumber: dto.Cert?.SerialNumber,
            subjectDn: dto.Cert?.SubjectDn,
            issuerDn: dto.Cert?.IssuerDn,
            validFrom: GeneralizedTime.TryParse(dto.Cert?.ValidFrom),
            validTo: GeneralizedTime.TryParse(dto.Cert?.ValidTo),
            keyEnabled: string.Equals(dto.Key?.Status, "ENABLED", StringComparison.OrdinalIgnoreCase),
            signAlgorithmOids: dto.Key?.Algo?.ToArray() ?? [],
            chain: chain);
    }
}
