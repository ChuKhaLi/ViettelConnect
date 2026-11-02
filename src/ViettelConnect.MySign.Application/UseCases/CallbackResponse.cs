using ViettelConnect.MySign.Domain.Certificates;
using ViettelConnect.MySign.Domain.Signing;

namespace ViettelConnect.MySign.Application.UseCases;

/// <summary>Phản hồi gửi lại Viettel. <c>JsonBody</c> đúng định dạng Viettel chờ (có thể chứa token nên <c>ToString</c> không in nó).</summary>
internal sealed record CallbackResponse(int HttpStatus, string JsonBody)
{
    public override string ToString() => $"CallbackResponse {{ HttpStatus = {HttpStatus} }}";
}

/// <summary>Kết cục ký do callback báo về. <c>Signatures</c> rỗng trừ khi <c>Status</c> là Succeeded.</summary>
internal sealed record CallbackSignOutcome(
    string TenantKey,
    string TransactionId,
    SignStatus Status,
    IReadOnlyList<DocumentSignature> Signatures,
    string? RawStatus)
{
    public override string ToString() =>
        $"CallbackSignOutcome {{ TenantKey = {TenantKey}, Status = {Status}, Signatures = {Signatures.Count} }}";
}

/// <summary>Kết quả bắt đầu ký bất đồng bộ: mã giao dịch để đối chiếu với callback và chứng thư đã chọn.</summary>
internal sealed record BeginSignResult(string TransactionId, Certificate Certificate);
