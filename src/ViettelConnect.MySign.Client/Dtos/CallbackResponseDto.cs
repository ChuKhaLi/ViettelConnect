namespace ViettelConnect.MySign.Client.Dtos;

/// <summary>
/// Phản hồi ứng dụng phải ghi nguyên văn cho Viettel ở endpoint callback: mã HTTP <see cref="HttpStatus"/> và thân
/// <see cref="JsonBody"/> (JSON UTF-8, <c>Content-Type: application/json</c>). Thân có thể chứa access token nên
/// <see cref="ToString"/> không in nó; đừng ghi log thân này.
/// </summary>
public sealed record CallbackResponseDto(int HttpStatus, string JsonBody)
{
    public override string ToString() => $"CallbackResponseDto {{ HttpStatus = {HttpStatus}, JsonBody = <redacted> }}";
}
