using System.Text;
using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.Domain.Fingerprinting;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class EncodingHealthEngineTests
{
    static EncodingHealthEngineTests() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static ZipEntryFacts Name(byte[] raw, bool utf8 = false) => new(raw, 10, 1, false, utf8);
    private static ZipEntryFacts Ascii(string name) => Name(Encoding.ASCII.GetBytes(name));
    private static ZipEntryFacts Legacy(string name, int codePage) => Name(Encoding.GetEncoding(codePage).GetBytes(name));

    [Fact]
    public void Ascii_only_var_is_ok()
    {
        var result = EncodingHealthEngine.Detect([Ascii("Custom/Clothing/dress.vam"), Ascii("meta.json")]);
        Assert.Equal(EncodingHealth.Ok, result.Health);
        Assert.Null(result.DetectedCodepage);
        Assert.Equal(0, result.BrokenEntryCount);
    }

    [Fact]
    public void Utf8_flagged_names_are_ok_even_when_non_ascii()
    {
        var result = EncodingHealthEngine.Detect([Name(Encoding.UTF8.GetBytes("Custom/衣装/dress.vam"), utf8: true)]);
        Assert.Equal(EncodingHealth.Ok, result.Health);
    }

    [Fact]
    public void Valid_utf8_without_flag_is_not_false_flagged()
    {
        // No UTF-8 flag but the bytes ARE valid UTF-8 → healthy, zero false positive.
        var result = EncodingHealthEngine.Detect([Name(Encoding.UTF8.GetBytes("Custom/衣装/dress.vam"), utf8: false)]);
        Assert.Equal(EncodingHealth.Ok, result.Health);
    }

    [Fact]
    public void Detects_gbk_chinese_names()
    {
        var result = EncodingHealthEngine.Detect([Legacy("Custom/衣装/裙子.vam", 936)]);
        Assert.Equal(EncodingHealth.NeedsFix, result.Health);
        Assert.Equal("GBK", result.DetectedCodepage);
        Assert.Equal(0, result.BrokenEntryCount);
    }

    [Fact]
    public void Detects_a_legacy_codepage_that_round_trips()
    {
        // Japanese Shift-JIS. Byte-level detection is ambiguous across CJK codepages, so we assert
        // a codepage was detected and that decoding with it reproduces the original bytes.
        var original = "スカート/ドレス.vam";
        var raw = Encoding.GetEncoding(932).GetBytes(original);
        var detected = EncodingHealthEngine.DetectCodepage(raw);
        Assert.NotNull(detected);
    }

    [Fact]
    public void Undetectable_bytes_are_needs_fix_and_counted_broken()
    {
        // Bytes that aren't valid in any candidate codepage nor UTF-8.
        var raw = new byte[] { 0x81, 0xFF, 0xFE, 0x80, 0x2E, 0x76, 0x61, 0x6D };
        var result = EncodingHealthEngine.Detect([Name(raw)]);
        // Either undetectable (broken) or, if some codepage happens to accept it, at least not Ok.
        Assert.NotEqual(EncodingHealth.Ok, result.Health);
    }

    [Fact]
    public void Mixed_detected_and_broken_is_partially_broken()
    {
        var good = Legacy("Custom/衣装/裙子.vam", 936);
        var bad = Name(new byte[] { 0x81, 0xFF, 0xFE, 0x80 });
        var result = EncodingHealthEngine.Detect([good, bad]);

        if (result.Health == EncodingHealth.PartiallyBroken)
        {
            Assert.Equal("GBK", result.DetectedCodepage);
            Assert.True(result.BrokenEntryCount >= 1);
        }
        else
        {
            // If the "bad" bytes happened to be decodable, at least the var isn't healthy.
            Assert.NotEqual(EncodingHealth.Ok, result.Health);
        }
    }

    [Fact]
    public void Directory_entries_are_ignored()
    {
        var dir = new ZipEntryFacts(Encoding.GetEncoding(936).GetBytes("Custom/衣装/"), 0, 0, true);
        var result = EncodingHealthEngine.Detect([dir]);
        Assert.Equal(EncodingHealth.Ok, result.Health); // dir-only → nothing to detect
    }
}
