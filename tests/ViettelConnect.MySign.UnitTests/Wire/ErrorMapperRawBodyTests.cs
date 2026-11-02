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
    public void Raw_body_is_omitted_when_not_included()
    {
        var ex = ErrorMapper.FromHttpError("login", 401, null, "{\"access_token\":\"tok-123\"}", includeRaw: false, "s", "corr");

        Assert.DoesNotContain("Raw response", ex.Detail);
    }
}
