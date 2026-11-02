namespace ViettelConnect.MySign.Application.Abstractions;

/// <summary>
/// Cho biết thông tin đăng nhập của lời gọi HIỆN TẠI. SDK gọi trong từng lời gọi (thân login,
/// khoá cache token) nên <see cref="System.Threading.AsyncLocal{T}"/> do bên dùng đặt quanh lời gọi
/// sẽ tách được từng người ký / từng đơn vị.
/// </summary>
/// <remarks>
/// Phải đăng ký singleton và đọc trạng thái ambient/options bên trong <see cref="Get"/>; không cache
/// kết quả, không scoped. Muốn thay mặc định, đăng ký trước hoặc sau khi gọi hàm đăng ký của SDK
/// (SDK dùng <c>TryAddSingleton</c>; đăng ký sau cùng thắng). Bắt buộc khi <c>CredentialsMode = Dynamic</c>.
/// SDK không bao giờ ghi log giá trị trả về.
/// </remarks>
public interface IMySignCredentialsAccessor
{
    /// <summary>Lấy thông tin đăng nhập cho lời gọi hiện tại.</summary>
    MySignCredentials Get();
}
