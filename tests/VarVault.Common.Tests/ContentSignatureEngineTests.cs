using System.Text;
using VarVault.Domain.Fingerprinting;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class ContentSignatureEngineTests
{
    private static ZipEntryFacts Entry(string name, long size, uint crc, bool dir = false) =>
        new(Encoding.UTF8.GetBytes(name), size, crc, dir);

    [Fact]
    public void Content_signature_is_order_independent()
    {
        var a = ContentSignatureEngine.Compute([Entry("a.vam", 10, 1), Entry("b.vaj", 20, 2), Entry("c.vab", 30, 3)]);
        var b = ContentSignatureEngine.Compute([Entry("c.vab", 30, 3), Entry("a.vam", 10, 1), Entry("b.vaj", 20, 2)]);
        Assert.Equal(a.ContentSignature, b.ContentSignature);
    }

    [Fact]
    public void Directory_entries_are_excluded()
    {
        var withDir = ContentSignatureEngine.Compute([Entry("Custom/", 0, 0, dir: true), Entry("a.vam", 10, 1)]);
        var withoutDir = ContentSignatureEngine.Compute([Entry("a.vam", 10, 1)]);
        Assert.Equal(withoutDir.ContentSignature, withDir.ContentSignature);
    }

    [Fact]
    public void Payload_signature_excludes_meta_json()
    {
        // Two vars identical except meta.json (size+crc differ) → same payload, different content.
        var v1 = ContentSignatureEngine.Compute([Entry("meta.json", 100, 111), Entry("a.vam", 10, 1)]);
        var v2 = ContentSignatureEngine.Compute([Entry("meta.json", 200, 222), Entry("a.vam", 10, 1)]);
        Assert.Equal(v1.PayloadSignature, v2.PayloadSignature);
        Assert.NotEqual(v1.ContentSignature, v2.ContentSignature);
    }

    [Fact]
    public void NoPath_signature_ignores_names_but_not_sizes()
    {
        // Same (size,crc) multiset, different names → same NoPath signature; different ContentSignature.
        var v1 = ContentSignatureEngine.Compute([Entry("original/name.vam", 10, 1)]);
        var v2 = ContentSignatureEngine.Compute([Entry("renamed/other.vam", 10, 1)]);
        Assert.Equal(v1.ContentSignatureNoPath, v2.ContentSignatureNoPath);
        Assert.NotEqual(v1.ContentSignature, v2.ContentSignature);

        var v3 = ContentSignatureEngine.Compute([Entry("original/name.vam", 99, 1)]);
        Assert.NotEqual(v1.ContentSignatureNoPath, v3.ContentSignatureNoPath);
    }

    [Fact]
    public void Signature_is_deterministic_and_hex_sha256()
    {
        var a = ContentSignatureEngine.Compute([Entry("a.vam", 10, 1)]);
        var b = ContentSignatureEngine.Compute([Entry("a.vam", 10, 1)]);
        Assert.Equal(a.ContentSignature, b.ContentSignature);
        Assert.Equal(64, a.ContentSignature.Length); // SHA-256 hex
    }

    [Fact]
    public void Raw_bytes_drive_identity_so_mojibake_is_deterministic()
    {
        // Same raw bytes (whatever codepage) → same signature, with no decoding involved.
        var raw = new byte[] { 0xC8, 0xB2, 0xD7, 0xB0, 0x2E, 0x76, 0x61, 0x6D }; // GBK-ish mojibake + ".vam"
        var v1 = ContentSignatureEngine.Compute([new ZipEntryFacts(raw, 10, 1, false)]);
        var v2 = ContentSignatureEngine.Compute([new ZipEntryFacts((byte[])raw.Clone(), 10, 1, false)]);
        Assert.Equal(v1.ContentSignature, v2.ContentSignature);
    }

    [Fact]
    public void Different_content_yields_different_signature()
    {
        var a = ContentSignatureEngine.Compute([Entry("a.vam", 10, 1)]);
        var b = ContentSignatureEngine.Compute([Entry("a.vam", 10, 2)]);
        Assert.NotEqual(a.ContentSignature, b.ContentSignature);
    }
}
