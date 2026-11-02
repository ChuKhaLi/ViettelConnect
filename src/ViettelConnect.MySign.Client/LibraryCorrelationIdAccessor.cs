using ViettelConnect.MySign.Application.Abstractions;

namespace ViettelConnect.MySign.Client;

/// <summary>Mặc định của <see cref="ICorrelationIdAccessor"/>: một GUID cho mỗi scope DI. Ứng dụng thay được (TryAdd).</summary>
internal sealed class LibraryCorrelationIdAccessor : ICorrelationIdAccessor
{
    public string Current { get; } = Guid.NewGuid().ToString("D");
}
