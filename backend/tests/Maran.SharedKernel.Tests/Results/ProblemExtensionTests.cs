using Maran.SharedKernel.Results;

namespace Maran.SharedKernel.Tests.Results;

/// <summary>
/// The problem-extension convention's one construction site: what a key must look like, and which
/// member names it can never claim — because a permitted <c>code</c> key would silently overwrite
/// the machine code every screen branches on.
/// </summary>
public sealed class ProblemExtensionTests
{
    /// <summary>A valid key and payload come back exactly as given.</summary>
    [Fact]
    public void Of_carries_the_key_and_the_payload()
    {
        var payload = new { DatabasesRestored = 1u };

        var extension = ProblemExtension.Of("restore", payload);

        Assert.Equal("restore", extension.Key);
        Assert.Same(payload, extension.Value);
    }

    /// <summary>Every reserved problem member name is refused as a key.</summary>
    /// <param name="reserved">The member name an extension must not shadow.</param>
    /// <remarks>
    /// The five are RFC 7807's own members and the two are what <c>ApiResultExtensions</c> writes on
    /// every failure; an extension under any of them corrupts the problem response rather than
    /// extending it. The accepting test above is this theory's inverse control — a guard mutated to
    /// refuse everything would fail there.
    /// </remarks>
    [Theory]
    [InlineData("type")]
    [InlineData("title")]
    [InlineData("status")]
    [InlineData("detail")]
    [InlineData("instance")]
    [InlineData("code")]
    [InlineData("correlationId")]
    public void Of_refuses_a_reserved_member_name(string reserved)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
        {
            return ProblemExtension.Of(reserved, new { });
        });

        Assert.Contains("reserved", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A key that is not a camelCase ASCII identifier is refused.</summary>
    /// <param name="key">The malformed candidate key.</param>
    /// <remarks>
    /// The shape rule is not cosmetic: it is what makes the reserved comparison above exact — a key
    /// like <c>" code"</c> or <c>"Code"</c> would slip an ordinal reserved check and still collide
    /// (or confuse) on the wire, so neither shape can be constructed at all.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Restore")]
    [InlineData(" code")]
    [InlineData("restore-progress")]
    [InlineData("restore.progress")]
    [InlineData("код")]
    public void Of_refuses_a_key_that_is_not_a_camel_case_identifier(string key)
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return ProblemExtension.Of(key, new { });
        });
    }

    /// <summary>A key with digits after the first letter is accepted.</summary>
    /// <remarks>
    /// The inverse control on the shape rule's other edge: the rule must refuse malformed keys, not
    /// everything beyond plain letters.
    /// </remarks>
    [Fact]
    public void Of_accepts_a_key_with_digits_after_the_first_letter()
    {
        var extension = ProblemExtension.Of("restoreV2", new { });

        Assert.Equal("restoreV2", extension.Key);
    }

    /// <summary>A null payload is refused; an extension with nothing to publish is not built.</summary>
    [Fact]
    public void Of_refuses_a_null_payload()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            return ProblemExtension.Of("restore", null!);
        });
    }
}
