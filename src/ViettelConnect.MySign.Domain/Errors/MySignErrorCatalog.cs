using static ViettelConnect.MySign.Domain.Errors.MySignErrorCategory;

namespace ViettelConnect.MySign.Domain.Errors;

/// <summary>
/// Bảng tĩnh mã lỗi Viettel MySign -> nhóm lỗi của SDK.
/// Nguồn: tài liệu MySign API+SDK v1.8, Phụ lục 1 mục "4. Danh sách mã lỗi chung";
/// mã 4xxx/13xxx từ "Bảng mã trạng thái meta_data.status" (signHash/requests/status);
/// 59033 và mã chuỗi (invalid_request, ...) từ các ví dụ phản hồi lỗi của login/signHash.
/// Mã không có trong bảng, hoặc lỗi phía máy chủ Viettel, là Unknown.
/// </summary>
public static class MySignErrorCatalog
{
    private static readonly Dictionary<string, MySignErrorCategory> Table = Build();

    public static MySignErrorCategory Classify(string? rawCode)
        => rawCode is not null && Table.TryGetValue(rawCode.Trim(), out var category) ? category : Unknown;

    private static Dictionary<string, MySignErrorCategory> Build()
    {
        var t = new Dictionary<string, MySignErrorCategory>(StringComparer.OrdinalIgnoreCase);

        void Add(MySignErrorCategory category, params string[] codes)
        {
            foreach (var code in codes)
            {
                t[code] = category;
            }
        }

        // Client / secret / profile / user / token / chứng chỉ TLS-request signing.
        Add(
            Authentication,
            "59033", "58006", "58007", "58008", "58029", "58032", "58037", "58038", "58040", "58041", "58079",
            "86000", "86001", "86002", "86003", "86004", "86005", "86006", "86007", "86008", "86009", "86010",
            "86011", "86015", "86016", "86017", "86018", "86019", "86020", "86021", "86022", "86023", "86024");

        // Thiếu / sai tham số.
        Add(
            Validation,
            "58011", "58012", "58013", "58014", "58015", "58016", "58017", "58018", "58019", "58020", "58021",
            "58022", "58023", "58024", "58025", "58026", "58027", "58028", "58030", "58031", "58033", "58034",
            "58035", "58036", "58039", "58042", "58043", "58044", "58055", "58056", "58057", "58058", "58059",
            "58060", "58063", "58064", "58065", "58066", "58067", "58068", "58069", "58070", "58071", "58080",
            "58082", "58089", "58090", "58091", "58092",
            "invalid_request", "invalid_csr", "invalid_otp", "missing_csr", "missing_device_id",
            "missing_device_info", "missing_device_name", "missing_request_id");

        // Khoá / chuỗi chứng thư.
        Add(CertificateInvalid, "58010", "58081", "13003", "13004", "4006", "4008");

        // Mobile: người dùng huỷ / hết giờ xác nhận / ký lỗi / hết lượt.
        Add(SignTimeout, "58061", "4001");
        Add(SignRejected, "58062", "4002");
        Add(SignFailed, "58083", "4004");
        Add(InsufficientQuota, "4005");

        return t;
    }
}
