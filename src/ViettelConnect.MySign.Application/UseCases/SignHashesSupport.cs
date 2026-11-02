using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Application.Signing;
using ViettelConnect.MySign.Domain.Certificates;
using ViettelConnect.MySign.Domain.Errors;

namespace ViettelConnect.MySign.Application.UseCases;

/// <summary>Phần dùng chung của các nhánh ký (đồng bộ, hỏi trạng thái, callback): kiểm tra yêu cầu, chọn chứng thư, dựng yêu cầu signHash.</summary>
internal static class SignHashesSupport
{
    private const int Sha256Length = 32;

    public static void Validate(SignHashesRequest request, ICorrelationIdAccessor correlation)
    {
        if (request.Documents is null || request.Documents.Count == 0)
        {
            throw Fail(correlation, MySignErrorCategory.Validation, "Cần ít nhất một tài liệu để ký.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < request.Documents.Count; i++)
        {
            var doc = request.Documents[i];
            if (doc is null || string.IsNullOrWhiteSpace(doc.DocumentId))
            {
                throw Fail(correlation, MySignErrorCategory.Validation, $"Tài liệu thứ {i + 1} thiếu DocumentId.");
            }

            if (doc.Sha256 is null || doc.Sha256.Length != Sha256Length)
            {
                throw Fail(correlation, MySignErrorCategory.Validation, $"Mã băm của tài liệu thứ {i + 1} phải đúng {Sha256Length} byte (SHA-256).");
            }

            if (!seen.Add(doc.DocumentId))
            {
                throw Fail(correlation, MySignErrorCategory.Validation, $"DocumentId của tài liệu thứ {i + 1} bị trùng.");
            }
        }
    }

    public static async Task<Certificate> SelectCertificateAsync(
        IMySignWireClient wire,
        MySignCredentials who,
        string? credentialId,
        ICertificateSelector selector,
        ICorrelationIdAccessor correlation,
        CancellationToken ct)
    {
        var certificates = await wire.GetCertificatesAsync(who, ct).ConfigureAwait(false);
        if (credentialId is { } id)
        {
            return certificates.FirstOrDefault(c => string.Equals(c.CredentialId, id, StringComparison.Ordinal))
                ?? throw Fail(correlation, MySignErrorCategory.NoActiveCertificate, "Chứng thư được yêu cầu không thuộc người ký này.");
        }

        return await selector.SelectAsync(certificates, ct).ConfigureAwait(false);
    }

    public static SignHashWireRequest BuildWireRequest(SignHashesRequest request, Certificate certificate, int asyncCode, string? sad) =>
        new(
            certificate.CredentialId,
            request.Documents
                .Select(d => (d.DocumentId, DisplayLabelEncoder.Encode(d.DisplayName, d.DocumentId), d.Sha256))
                .ToArray(),
            string.IsNullOrWhiteSpace(request.Description) ? null : DisplayLabelEncoder.Encode(request.Description, "Document"),
            asyncCode,
            sad);

    public static MySignException Fail(ICorrelationIdAccessor correlation, MySignErrorCategory category, string detail) =>
        new(category, rawCode: null, detail, correlation.Current);
}
