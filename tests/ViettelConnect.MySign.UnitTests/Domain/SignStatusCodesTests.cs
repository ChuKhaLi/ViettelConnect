using ViettelConnect.MySign.Domain.Signing;
using Xunit;

namespace ViettelConnect.MySign.UnitTests.Domain;

public sealed class SignStatusCodesTests
{
    [Theory]
    [InlineData("1", SignStatus.Succeeded)]
    [InlineData("4000", SignStatus.Pending)]
    [InlineData("4003", SignStatus.Pending)]
    [InlineData("4007", SignStatus.Pending)]
    [InlineData("6000", SignStatus.Pending)]
    [InlineData("4001", SignStatus.TimedOut)]
    [InlineData("4002", SignStatus.Rejected)]
    [InlineData("4004", SignStatus.Failed)]
    [InlineData("4005", SignStatus.Failed)]
    [InlineData("4006", SignStatus.Failed)]
    [InlineData("4008", SignStatus.Failed)]
    [InlineData("50000", SignStatus.Failed)]
    [InlineData("13003", SignStatus.Failed)]
    [InlineData("13004", SignStatus.Failed)]
    [InlineData("4999", SignStatus.Pending)]
    [InlineData("", SignStatus.Pending)]
    [InlineData(null, SignStatus.Pending)]
    public void Classify_maps_code(string? code, SignStatus expected)
        => Assert.Equal(expected, SignStatusCodes.Classify(code));
}
