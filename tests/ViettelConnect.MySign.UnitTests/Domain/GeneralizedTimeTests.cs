using ViettelConnect.MySign.Domain.Certificates;
using Xunit;

namespace ViettelConnect.MySign.UnitTests.Domain;

public sealed class GeneralizedTimeTests
{
    [Theory]
    [InlineData("20180709132216+0000", "2018-07-09T13:22:16Z")]
    [InlineData("20190709132216Z", "2019-07-09T13:22:16Z")]
    [InlineData("20180709132216-0700", "2018-07-09T20:22:16Z")]
    public void TryParse_parses_valid(string input, string expected)
        => Assert.Equal(DateTimeOffset.Parse(expected), GeneralizedTime.TryParse(input));

    [Theory]
    [InlineData("garbage")]
    [InlineData("20180709132216+1459")]
    [InlineData("20180709132216-1500")]
    [InlineData("00010101000000+0100")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_returns_null_for_invalid(string? input)
        => Assert.Null(GeneralizedTime.TryParse(input));
}
