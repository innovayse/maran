using System.Text;
using Maran.Packaging.Services;
using Maran.Packaging.Tests.Builders;

namespace Maran.Packaging.Tests.Services;

/// <summary>
/// Reading and writing <c>ipm.json</c>. The reading is strict on purpose: each way a signed file could be
/// read differently by two parsers is refused, and the good file beside them is read.
/// </summary>
public sealed class IpmManifestCodecTests : IDisposable
{
    /// <summary>The throwaway key used only to have a good manifest to start from.</summary>
    private readonly System.Security.Cryptography.ECDsa _key = IpmFixtureBuilder.NewKey();

    /// <summary>Frees the throwaway key.</summary>
    public void Dispose()
    {
        _key.Dispose();
    }

    /// <summary>A written manifest reads back, and writing it again gives the same bytes.</summary>
    [Fact]
    public void A_written_manifest_reads_back_and_writes_the_same_bytes_again()
    {
        var manifest = new IpmFixtureBuilder(_key).BuildManifest();
        var written = IpmManifestCodec.Serialize(manifest);

        var read = IpmManifestCodec.Parse(written);

        Assert.True(read.IsSuccess, read.Error?.Code);
        Assert.Equal("hello", read.Value.Id);
        Assert.Equal(manifest.Files.Count, read.Value.Files.Count);
        Assert.Equal(written, IpmManifestCodec.Serialize(read.Value));
    }

    /// <summary>The keys are written in the record's declaration order, camel-cased, with no whitespace.</summary>
    [Fact]
    public void Keys_are_written_in_a_fixed_order_without_whitespace()
    {
        var text = Encoding.UTF8.GetString(IpmManifestCodec.Serialize(new IpmFixtureBuilder(_key).BuildManifest()));

        Assert.StartsWith("{\"formatVersion\":1,\"id\":\"hello\",\"displayNameKey\":\"HelloName\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain(": ", text, StringComparison.Ordinal);
        Assert.DoesNotContain(", ", text, StringComparison.Ordinal);
    }

    /// <summary>A format revision this library does not read is named as such, before any other rule.</summary>
    [Fact]
    public void An_unknown_format_version_is_named_as_such_even_when_the_rest_is_unreadable()
    {
        RefusalAssert.Is("IpmFormatVersionUnknown", IpmManifestCodec.Parse(Bytes("{\"formatVersion\":2,\"surprise\":[1,2]}")).Error);
        RefusalAssert.Is("IpmFormatVersionUnknown", IpmManifestCodec.Parse(Bytes("{\"formatVersion\":0}")).Error);
    }

    /// <summary>Every way the document can fail to be a manifest is refused as malformed.</summary>
    /// <param name="document">A document that is not a manifest.</param>
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"formatVersion\":\"1\"}")]
    public void A_document_that_is_not_a_manifest_is_refused_as_malformed(string document)
    {
        RefusalAssert.Is("IpmManifestMalformed", IpmManifestCodec.Parse(Bytes(document)).Error);
    }

    /// <summary>A member repeated inside one object is refused, escaped spelling included.</summary>
    /// <param name="document">A document naming a member twice.</param>
    [Theory]
    [InlineData("{\"formatVersion\":1,\"formatVersion\":1}")]
    [InlineData("{\"formatVersion\":1,\"id\":\"a\",\"\\u0069d\":\"b\"}")]
    public void A_member_named_twice_is_refused(string document)
    {
        RefusalAssert.Is("IpmManifestMalformed", IpmManifestCodec.Parse(Bytes(document)).Error);
    }

    /// <summary>An unknown member, a missing required member, a null and a number for a name are all refused.</summary>
    /// <param name="find">The text of a good document to change.</param>
    /// <param name="replace">What it becomes.</param>
    [Theory]
    [InlineData("{\"formatVersion\":1,", "{\"formatVersion\":1,\"extra\":true,")]
    [InlineData("\"id\":\"hello\",", "")]
    [InlineData("\"displayNameKey\":\"HelloName\"", "\"displayNameKey\":null")]
    [InlineData("\"tier\":\"AddOn\"", "\"tier\":1")]
    [InlineData("\"tier\":\"AddOn\"", "\"tier\":\"Platinum\"")]
    public void A_strictness_violation_in_an_otherwise_good_document_is_refused(string find, string replace)
    {
        var good = Encoding.UTF8.GetString(IpmManifestCodec.Serialize(new IpmFixtureBuilder(_key).BuildManifest()));
        Assert.Contains(find, good, StringComparison.Ordinal);

        var changed = good.Replace(find, replace, StringComparison.Ordinal);

        RefusalAssert.Is("IpmManifestMalformed", IpmManifestCodec.Parse(Bytes(changed)).Error);
        Assert.True(IpmManifestCodec.Parse(Bytes(good)).IsSuccess);
    }

    /// <summary>UTF-8 bytes of some text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Its bytes.</returns>
    private static byte[] Bytes(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }
}
