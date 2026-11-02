using ViettelConnect.MySign.Domain.Errors;
using Xunit;

namespace ViettelConnect.MySign.UnitTests.Domain;

public sealed class MySignExceptionTests
{
    [Fact]
    public void Exception_carries_fields()
    {
        var ex = new MySignTimeoutException("x", "cid", "tx1");
        Assert.Equal(MySignErrorCategory.SignTimeout, ex.Category);
        Assert.Equal("tx1", ex.TransactionId);
        Assert.Equal("cid", ex.CorrelationId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Blank_correlation_id_falls_back_to_a_fresh_guid_instead_of_masking_the_error(string? blank)
    {
        var ex = new MySignException(MySignErrorCategory.SignRejected, "4002", "detail", blank!);

        Assert.Equal(MySignErrorCategory.SignRejected, ex.Category);
        Assert.Equal("4002", ex.RawCode);
        Assert.Equal("detail", ex.Detail);
        Assert.Matches("^[0-9a-f]{32}$", ex.CorrelationId);
        Assert.NotEqual(ex.CorrelationId, new MySignException(MySignErrorCategory.Unknown, null, "d", blank!).CorrelationId);
    }
}
