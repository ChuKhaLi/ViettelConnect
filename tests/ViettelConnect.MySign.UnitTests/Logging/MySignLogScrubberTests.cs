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

    [Theory]
    [InlineData("{\"client_secret\":\"ab\\\"cd-tail\"}", "cd-tail")]
    [InlineData("{\"access_token\":\"x\\\\\\\"leak-me\",\"y\":1}", "leak-me")]
    [InlineData("{\"signatures\":[\"a]b\",\"after-bracket\"]}", "after-bracket")]
    public void Escaped_quotes_inside_sensitive_strings_do_not_leak_the_tail(string input, string secret)
    {
        var output = MySignLogScrubber.Scrub(input);

        Assert.DoesNotContain(secret, output);
        Assert.Contains("<redacted>", output);
    }

    [Theory]
    [InlineData("{\"user_id\":123456789,\"x\":1}", "123456789")]
    [InlineData("{\"profile_id\": -42.5e3}", "-42.5e3")]
    [InlineData("{\"sad\":true}", "true")]
    [InlineData("{\"token\":null}", "null")]
    public void Unquoted_scalar_values_of_sensitive_keys_are_redacted(string input, string secret)
    {
        var output = MySignLogScrubber.Scrub(input);

        Assert.DoesNotContain(secret, output);
        Assert.Contains("<redacted>", output);
    }

    [Fact]
    public void Non_sensitive_keys_keep_their_scalar_values()
        => Assert.Equal("{\"num_signatures\":2,\"async\":0}", MySignLogScrubber.Scrub("{\"num_signatures\":2,\"async\":0}"));

    [Fact]
    public void Guid_n_and_32_hex_identifiers_survive_the_base64_rule()
    {
        var guid = Guid.NewGuid().ToString("N");
        const string upperHex = "0123456789ABCDEF0123456789ABCDEF";

        var output = MySignLogScrubber.Scrub($"correlationId={guid} transactionId={upperHex}");

        Assert.Contains(guid, output);
        Assert.Contains(upperHex, output);
    }

    [Fact]
    public void Real_base64_signatures_and_tokens_are_still_redacted()
    {
        var shortB64 = Convert.ToBase64String(Enumerable.Range(0, 24).Select(i => (byte)(i * 37 + 11)).ToArray());
        var signature = Convert.ToBase64String(Enumerable.Range(0, 256).Select(i => (byte)(i * 7 + 3)).ToArray());
        const string hex40 = "205640fd6ea8c7d80bb91c630b52d286d21ee511";
        Assert.Equal(32, shortB64.Length);

        var output = MySignLogScrubber.Scrub($"a {shortB64} b {signature} c {hex40}");

        Assert.DoesNotContain(shortB64, output);
        Assert.DoesNotContain(signature, output);
        Assert.DoesNotContain(hex40, output);
    }
}
