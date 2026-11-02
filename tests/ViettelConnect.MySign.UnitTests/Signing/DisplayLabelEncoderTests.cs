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
    [InlineData("a_b-c d", "doc", "a_b-c d")]
    [InlineData("__init__--x", "doc", "__init__--x")]
    public void Encode_normalizes(string? label, string fallback, string expected)
        => Assert.Equal(expected, Decode(DisplayLabelEncoder.Encode(label, fallback)));

    // Không dùng InlineData: chuỗi trong attribute được lưu dạng UTF-8 nên surrogate lẻ bị thay bằng U+FFFD.
    public static TheoryData<string, string, string> LoneSurrogates => new()
    {
        { "\ud800abc", "doc", "abc" },
        { "\udc00", "doc", "doc" },
        { "\udc00", "\ud800", "Document" },
        { "a\udc00\ud800b", "doc", "a b" },
        { "x\U0001F600y", "doc", "x y" },
    };

    [Theory]
    [MemberData(nameof(LoneSurrogates))]
    public void Encode_lone_surrogates_do_not_throw(string label, string fallback, string expected)
    {
        Assert.Contains(label, char.IsSurrogate);
        Assert.Equal(expected, Decode(DisplayLabelEncoder.Encode(label, fallback)));
    }

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

    [Fact]
    public void Encode_long_label_hits_the_72_character_cap()
    {
        const string label = "Biên bản thí nghiệm máy biến áp 110kV — Trạm Đồng Hới số 2/2026 — lần đo lặp lại thứ ba";
        var decoded = Decode(DisplayLabelEncoder.Encode(label, "doc"));

        // Nếu không bị cắt, bản đã chuẩn hoá dài hơn 72; ở đây phải cắt đúng 72 (ký tự thứ 72 không phải dấu cách).
        Assert.Equal(72, decoded.Length);
    }

    [Theory]
    [InlineData(72, 72)]
    [InlineData(73, 72)]
    [InlineData(200, 72)]
    [InlineData(71, 71)]
    public void Encode_caps_at_72_source_characters_and_stays_under_100_encoded(int length, int expectedLength)
    {
        var encoded = DisplayLabelEncoder.Encode(new string('a', length), "doc");

        Assert.Equal(expectedLength, Decode(encoded).Length);
        Assert.True(encoded.Length < 100);
        Assert.Equal(96, DisplayLabelEncoder.Encode(new string('a', 72), "doc").Length);
    }

    [Fact]
    public void Encode_cut_landing_on_a_space_trims_it()
    {
        // 71 ký tự + dấu cách ở vị trí 72 + phần còn lại: sau khi cắt còn "aaa...a " nên phải bỏ dấu cách cuối.
        var label = new string('a', 71) + " " + new string('b', 10);
        var decoded = Decode(DisplayLabelEncoder.Encode(label, "doc"));

        Assert.Equal(new string('a', 71), decoded);
    }

    [Fact]
    public void Encode_cut_just_before_a_word_keeps_the_72_characters()
    {
        var label = new string('a', 72) + " " + new string('b', 10);
        var decoded = Decode(DisplayLabelEncoder.Encode(label, "doc"));

        Assert.Equal(new string('a', 72), decoded);
    }
}
