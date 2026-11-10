using Maran.Host.Modules;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Maran.Host.Tests.Modules;

/// <summary>
/// Behavioral contract of <see cref="ModuleFrameHeaders"/>: the exact policy of a frame, the headers every
/// answer carries, and the cache policy that differs between the document and a file.
/// </summary>
public sealed class ModuleFrameHeadersTests
{
    /// <summary>The frame's content security policy is exactly the contract's string.</summary>
    [Fact]
    public void The_frame_content_security_policy_is_exactly_the_contracts_string()
    {
        Assert.Equal(
            "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; "
            + "connect-src 'none'; frame-ancestors 'self'; base-uri 'none'; form-action 'none'",
            ModuleFrameHeaders.ContentSecurityPolicy);
    }

    /// <summary>The document carries the frame policy, nosniff, CORS and no-store, and no framing header.</summary>
    [Fact]
    public void The_document_carries_the_frame_policy_nosniff_cors_and_no_store()
    {
        var headers = new HeaderDictionary();

        ModuleFrameHeaders.ApplyToDocument(headers);

        Assert.Equal(ModuleFrameHeaders.ContentSecurityPolicy, headers[HeaderNames.ContentSecurityPolicy].ToString());
        Assert.Equal("nosniff", headers[HeaderNames.XContentTypeOptions].ToString());
        Assert.Equal("*", headers[HeaderNames.AccessControlAllowOrigin].ToString());
        Assert.Equal("no-store", headers[HeaderNames.CacheControl].ToString());
        Assert.False(headers.ContainsKey("X-Frame-Options"));
    }

    /// <summary>A file carries the same policy and is cached for a year as immutable, because its URL carries the version.</summary>
    [Fact]
    public void A_file_carries_the_frame_policy_and_is_cached_as_immutable()
    {
        var headers = new HeaderDictionary();

        ModuleFrameHeaders.ApplyToAsset(headers);

        Assert.Equal(ModuleFrameHeaders.ContentSecurityPolicy, headers[HeaderNames.ContentSecurityPolicy].ToString());
        Assert.Equal("nosniff", headers[HeaderNames.XContentTypeOptions].ToString());
        Assert.Equal("*", headers[HeaderNames.AccessControlAllowOrigin].ToString());
        Assert.Equal("public, max-age=31536000, immutable", headers[HeaderNames.CacheControl].ToString());
        Assert.False(headers.ContainsKey("X-Frame-Options"));
    }

    /// <summary>The frame policy replaces the shell's policy already on the response, and removes a framing ban.</summary>
    [Fact]
    public void The_frame_policy_replaces_the_shell_policy_and_removes_a_framing_ban()
    {
        var headers = new HeaderDictionary
        {
            [HeaderNames.ContentSecurityPolicy] = "default-src 'self'; frame-ancestors 'none'",
            ["X-Frame-Options"] = "DENY",
        };

        ModuleFrameHeaders.ApplyToAsset(headers);

        Assert.Equal(ModuleFrameHeaders.ContentSecurityPolicy, headers[HeaderNames.ContentSecurityPolicy].ToString());
        Assert.DoesNotContain("frame-ancestors 'none'", headers[HeaderNames.ContentSecurityPolicy].ToString(), StringComparison.Ordinal);
        Assert.False(headers.ContainsKey("X-Frame-Options"));
    }
}
