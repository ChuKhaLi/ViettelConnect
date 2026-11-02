using System.Text;
using System.Text.RegularExpressions;
using ViettelConnect.MySign.Application.Signing;
using Xunit;

namespace ViettelConnect.MySign.UnitTests.Signing;

public sealed class DisplayLabelEncoderTests
{
    private static string Decode(string encoded) => Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

    [Theory]
    [InlineData("Biên bản số 12", "doc", "Bien ban so 12")]
    [InlineData("Đồng Hới", "doc", "Dong Hoi")]
    [InlineData("đồng hới", "doc", "dong hoi")]
    [InlineData("A/B:C*D", "doc", "A B C D")]
    [InlineData("  a   b  ", "doc", "a b")]
    [InlineData("###", "Document 7", "Document 7")]
    [InlineData(null, "doc", "doc")]
    [InlineData("", "doc", "doc")]
    [InlineData("   ", "doc", "doc")]
    [InlineData("###", "###", "Document")]
    public void Encode_normalizes(string? label, string fallback, string expected)
        => Assert.Equal(expected, Decode(DisplayLabelEncoder.Encode(label, fallback)));

    [Fact]
    public void Encode_long_vietnamese_label_stays_under_100_and_valid()
    {
        const string label = "Biên bản thí nghiệm máy biến áp 110kV — Trạm Đồng Hới số 2/2026 — lần đo lặp lại thứ ba";
        var result = DisplayLabelEncoder.Encode(label, "doc");

        Assert.True(result.Length < 100);
        var decoded = Decode(result);
        Assert.Matches(new Regex(@"^[A-Za-z0-9_\- ]+$"), decoded);
        Assert.StartsWith(decoded, "Bien ban thi nghiem may bien ap 110kV Tram Dong Hoi so 2 2026 lan do lap lai thu ba");
        Assert.False(decoded.EndsWith(' '));
    }
}
