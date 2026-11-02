namespace ViettelConnect.MySign.Domain.Signing;

/// <summary>Phân loại meta_data.status của yêu cầu ký (tài liệu Viettel, mục Bảng mã trạng thái).</summary>
internal static class SignStatusCodes
{
    public static SignStatus Classify(string? code) => code switch
    {
        "1" => SignStatus.Succeeded,
        "4001" => SignStatus.TimedOut,
        "4002" => SignStatus.Rejected,
        "4004" or "4005" or "4006" or "4008" or "13003" or "13004" or "50000" => SignStatus.Failed,
        // 4000, 4003, 4007, 6000 và mọi mã lạ/rỗng: coi là đang chờ (không bao giờ coi là thành công).
        _ => SignStatus.Pending,
    };
}
