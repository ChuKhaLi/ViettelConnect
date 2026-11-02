using ViettelConnect.MySign.Domain.Signing;

namespace ViettelConnect.MySign.Application.Abstractions;

/// <summary>
/// Cài đặt ký mà use case cần, thuần tầng Application (Infrastructure ánh xạ từ <c>MySignOptions</c>).
/// Task 8 sẽ bổ sung các trường hỏi trạng thái.
/// </summary>
internal sealed record MySignSigningSettings(SignMode SignMode);
