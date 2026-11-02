using ViettelConnect.MySign.Application.Abstractions;

namespace ViettelConnect.MySign.Infrastructure.Wire;

/// <summary>
/// Mã tương quan "mượn" trong một luồng async. Dùng khi handler xác thực tự đăng nhập: wire client và logger dùng cho
/// lượt đăng nhập đó được lấy từ scope của handler, nên phải ghi log/ném lỗi với mã của request gốc đang chờ token.
/// </summary>
internal static class AmbientCorrelation
{
    private static readonly AsyncLocal<string?> Override = new();

    /// <summary>Mã đang mượn nếu có, ngược lại mã của <paramref name="accessor"/>.</summary>
    public static string Resolve(ICorrelationIdAccessor accessor) =>
        Override.Value is { Length: > 0 } id ? id : accessor.Current;

    /// <summary>Mượn <paramref name="correlationId"/> cho tới khi dispose (khôi phục giá trị trước đó).</summary>
    public static IDisposable Use(string correlationId)
    {
        var previous = Override.Value;
        Override.Value = correlationId;
        return new Restore(previous);
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => Override.Value = previous;
    }
}
