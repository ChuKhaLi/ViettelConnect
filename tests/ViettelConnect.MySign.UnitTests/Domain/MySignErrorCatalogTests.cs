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
    [InlineData("4001", MySignErrorCategory.SignTimeout)]
    [InlineData("4002", MySignErrorCategory.SignRejected)]
    [InlineData("4005", MySignErrorCategory.InsufficientQuota)]
    [InlineData(" INVALID_REQUEST ", MySignErrorCategory.Validation)]
    [InlineData("zzz", MySignErrorCategory.Unknown)]
    [InlineData(null, MySignErrorCategory.Unknown)]
    public void Classify_maps_code(string? code, MySignErrorCategory expected)
        => Assert.Equal(expected, MySignErrorCatalog.Classify(code));
}
