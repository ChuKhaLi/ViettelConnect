using ViettelConnect.MySign.Domain.Signing;
using Xunit;

namespace ViettelConnect.MySign.UnitTests.Domain;

public sealed class SignStatusCodesTests
{
    [Theory]
    [InlineData("1", nameof(SignStatus.Succeeded))]
    [InlineData("4000", nameof(SignStatus.Pending))]
    [InlineData("4003", nameof(SignStatus.Pending))]
    [InlineData("4007", nameof(SignStatus.Pending))]
    [InlineData("6000", nameof(SignStatus.Pending))]
    [InlineData("4001", nameof(SignStatus.TimedOut))]
    [InlineData("4002", nameof(SignStatus.Rejected))]
    [InlineData("4004", nameof(SignStatus.Failed))]
    [InlineData("4005", nameof(SignStatus.Failed))]
    [InlineData("4006", nameof(SignStatus.Failed))]
    [InlineData("4008", nameof(SignStatus.Failed))]
    [InlineData("50000", nameof(SignStatus.Failed))]
    [InlineData("13003", nameof(SignStatus.Failed))]
    [InlineData("13004", nameof(SignStatus.Failed))]
    [InlineData("4999", nameof(SignStatus.Pending))]
    [InlineData("", nameof(SignStatus.Pending))]
    [InlineData(null, nameof(SignStatus.Pending))]
    public void Classify_maps_code(string? code, string expected)
        => Assert.Equal(Enum.Parse<SignStatus>(expected), SignStatusCodes.Classify(code));
}
