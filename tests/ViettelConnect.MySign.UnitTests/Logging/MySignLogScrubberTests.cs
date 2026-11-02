using ViettelConnect.MySign.Infrastructure.Logging;
using Xunit;

namespace ViettelConnect.MySign.UnitTests.Logging;

public class MySignLogScrubberTests
{
    [Theory]
    [InlineData("{\"client_secret\":\"abc\"}", "abc")]
    [InlineData("{\"access_token\":\"xyz\"}", "xyz")]
    [InlineData("{\"refresh_token\":\"rrr\"}", "rrr")]
    [InlineData("Authorization: Bearer xyz", "xyz")]
    [InlineData("header Bearer xyz trailing", "xyz")]
    [InlineData("{\"sad\":\"sad-value\"}", "sad-value")]
    [InlineData("{\"user_id\":\"u-123\",\"x\":1}", "u-123")]
    [InlineData("{\"profile_id\":\"p-456\"}", "p-456")]
    [InlineData("{\"signatures\":[\"c2ln\",\"c2lnMg==\"]}", "c2lnMg==")]
    [InlineData("{\"hash\":[\"aGFzaA==\"]}", "aGFzaA==")]
    [InlineData("{\"signature\":\"c2ln\"}", "c2ln")]
    public void Sensitive_values_are_replaced_with_redacted(string input, string secret)
    {
        var output = MySignLogScrubber.Scrub(input);

        Assert.DoesNotContain(secret, output);
        Assert.Contains("<redacted>", output);
    }

    [Fact]
    public void Field_names_survive_so_logs_stay_readable()
    {
        var output = MySignLogScrubber.Scrub("{\"client_id\":\"lab-1\",\"client_secret\":\"abc\"}");

        Assert.Contains("\"client_id\":\"lab-1\"", output);
        Assert.Contains("\"client_secret\":\"<redacted>\"", output);
    }

    [Fact]
    public void Long_base64_runs_are_redacted_in_free_text()
    {
        var blob = Convert.ToBase64String(new byte[48]);

        Assert.DoesNotContain(blob, MySignLogScrubber.Scrub("payload " + blob));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Null_or_empty_is_returned_unchanged(string? input) =>
        Assert.Equal(input, MySignLogScrubber.Scrub(input!));

    [Fact]
    public void Plain_text_is_unchanged() =>
        Assert.Equal(
            "MySign login failed (HTTP 401, code 59033).",
            MySignLogScrubber.Scrub("MySign login failed (HTTP 401, code 59033)."));
}
