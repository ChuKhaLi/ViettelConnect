using ViettelConnect.MySign.Domain.Errors;
using ViettelConnect.MySign.Infrastructure.Wire;
using ViettelConnect.MySign.Infrastructure.Wire.Mapping;
using Xunit;

namespace ViettelConnect.MySign.UnitTests.Wire;

public class ErrorMapperRawBodyTests
{
    [Fact]
    public void Raw_body_redacts_secret_and_access_token_when_included()
    {
        var raw = "{\"error\":\"59033\",\"access_token\":\"tok-123\",\"note\":\"S3cr3t-xyz leaked\"}";

        var ex = ErrorMapper.FromHttpError("login", 401, new ErrorDto { Error = "59033" }, raw, includeRaw: true, "S3cr3t-xyz", "corr");

        Assert.DoesNotContain("tok-123", ex.Detail);
        Assert.DoesNotContain("S3cr3t-xyz", ex.Detail);
        Assert.Contains("<redacted>", ex.Detail);
    }

    [Fact]
    public void Sign_timeout_code_yields_timeout_exception()
    {
        var ex = ErrorMapper.FromHttpError("signHash", 400, new ErrorDto { Error = "58061" }, null, includeRaw: false, "s", "corr");

        var timeout = Assert.IsType<MySignTimeoutException>(ex);
        Assert.Equal(MySignErrorCategory.SignTimeout, timeout.Category);
        Assert.Equal("58061", timeout.RawCode);
        Assert.Equal("corr", timeout.CorrelationId);
    }

    [Theory]
    [InlineData(500, null, MySignErrorCategory.Transport)]
    [InlineData(503, "server_error", MySignErrorCategory.Transport)]
    [InlineData(429, null, MySignErrorCategory.Transport)]
    [InlineData(500, "58083", MySignErrorCategory.SignFailed)]
    [InlineData(502, "58062", MySignErrorCategory.SignRejected)]
    [InlineData(404, null, MySignErrorCategory.Unknown)]
    public void Transient_status_without_known_code_is_transport(int status, string? code, MySignErrorCategory expected)
    {
        var dto = code is null ? null : new ErrorDto { Error = code };

        var ex = ErrorMapper.FromHttpError("signHash", status, dto, null, includeRaw: false, "s", "corr");

        Assert.Equal(expected, ex.Category);
    }

    [Fact]
    public void Raw_body_is_omitted_when_not_included()
    {
        var ex = ErrorMapper.FromHttpError("login", 401, null, "{\"access_token\":\"tok-123\"}", includeRaw: false, "s", "corr");

        Assert.DoesNotContain("Raw response", ex.Detail);
    }
}
