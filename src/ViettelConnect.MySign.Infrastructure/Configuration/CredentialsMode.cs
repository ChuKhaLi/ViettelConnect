namespace ViettelConnect.MySign.Infrastructure.Configuration;

public enum CredentialsMode
{
    /// <summary>Thông tin đăng nhập lấy từ options; validator yêu cầu đủ khi khởi động.</summary>
    Static,

    /// <summary>Thông tin đăng nhập do <c>IMySignCredentialsAccessor</c> cấp theo từng lời gọi.</summary>
    Dynamic,
}
