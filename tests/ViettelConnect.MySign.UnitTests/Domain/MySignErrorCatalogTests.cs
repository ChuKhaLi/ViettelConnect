using ViettelConnect.MySign.Domain.Errors;
using Xunit;

namespace ViettelConnect.MySign.UnitTests.Domain;

public sealed class MySignErrorCatalogTests
{
    [Theory]
    [InlineData("59033", MySignErrorCategory.Authentication)]
    [InlineData("86024", MySignErrorCategory.Authentication)]
    [InlineData("58008", MySignErrorCategory.Authentication)]
    [InlineData("58058", MySignErrorCategory.Validation)]
    [InlineData("invalid_request", MySignErrorCategory.Validation)]
    [InlineData("zzz", MySignErrorCategory.Unknown)]
    [InlineData(null, MySignErrorCategory.Unknown)]
    public void Classify_maps_code(string? code, MySignErrorCategory expected)
        => Assert.Equal(expected, MySignErrorCatalog.Classify(code));

    [Fact]
    public void Exception_carries_fields_and_rejects_blank_correlation_id()
    {
        var ex = new MySignTimeoutException("x", "cid", "tx1");
        Assert.Equal(MySignErrorCategory.SignTimeout, ex.Category);
        Assert.Equal("tx1", ex.TransactionId);
        Assert.Equal("cid", ex.CorrelationId);
        Assert.Throws<ArgumentException>(() => new MySignException(MySignErrorCategory.Unknown, null, "d", " "));
    }
}
