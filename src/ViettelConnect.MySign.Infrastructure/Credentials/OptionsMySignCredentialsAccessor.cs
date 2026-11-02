using Microsoft.Extensions.Options;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Infrastructure.Configuration;

namespace ViettelConnect.MySign.Infrastructure.Credentials;

/// <summary>
/// Mặc định của <see cref="IMySignCredentialsAccessor"/>: trả bốn giá trị từ <see cref="MySignOptions"/>.
/// Không rẽ nhánh theo <see cref="MySignOptions.CredentialsMode"/>; chế độ Dynamic dựa vào accessor
/// do bên dùng đăng ký thay thế.
/// </summary>
internal sealed class OptionsMySignCredentialsAccessor : IMySignCredentialsAccessor
{
    private readonly IOptions<MySignOptions> _options;

    public OptionsMySignCredentialsAccessor(IOptions<MySignOptions> options) => _options = options;

    public MySignCredentials Get()
    {
        var o = _options.Value;
        return new MySignCredentials(o.ClientId, o.ClientSecret, o.ProfileId, o.UserId);
    }
}
