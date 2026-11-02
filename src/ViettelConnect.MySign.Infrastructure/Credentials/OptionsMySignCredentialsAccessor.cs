using Microsoft.Extensions.Options;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Infrastructure.Configuration;

namespace ViettelConnect.MySign.Infrastructure.Credentials;

/// <summary>
/// Mặc định của <see cref="IMySignCredentialsAccessor"/>: trả bốn giá trị từ <see cref="MySignOptions"/>.
/// Ở chế độ <see cref="CredentialsMode.Dynamic"/>, options không chứa thông tin đăng nhập nên mặc định này ném lỗi rõ ràng
/// thay vì âm thầm gửi thông tin rỗng: ứng dụng phải đăng ký <see cref="IMySignCredentialsAccessor"/> của mình
/// (trước hay sau khi gọi hàm đăng ký của SDK đều được).
/// </summary>
internal sealed class OptionsMySignCredentialsAccessor : IMySignCredentialsAccessor
{
    private readonly IOptions<MySignOptions> _options;

    public OptionsMySignCredentialsAccessor(IOptions<MySignOptions> options) => _options = options;

    public MySignCredentials Get()
    {
        var o = _options.Value;
        if (o.CredentialsMode == CredentialsMode.Dynamic)
        {
            throw new InvalidOperationException(
                $"{MySignOptions.SectionName}:CredentialsMode is Dynamic but no application {nameof(IMySignCredentialsAccessor)} is registered. " +
                $"Register your own {nameof(IMySignCredentialsAccessor)} (singleton) that returns the current signer's credentials.");
        }

        return new MySignCredentials(o.ClientId, o.ClientSecret, o.ProfileId, o.UserId);
    }
}
