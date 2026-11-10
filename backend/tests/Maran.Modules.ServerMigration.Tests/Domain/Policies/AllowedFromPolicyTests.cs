using Maran.Modules.ServerMigration.Domain.Policies;

namespace Maran.Modules.ServerMigration.Tests.Domain.Policies;

/// <summary>
/// The key restriction is rebuilt from parsed ranges, so only canonical address text ever reaches the line the
/// administrator installs.
/// </summary>
public sealed class AllowedFromPolicyTests
{
    /// <summary>No text means no restriction, and is not a refusal.</summary>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_text_means_no_restriction(string? text)
    {
        var read = AllowedFromPolicy.TryRead(text, out var canonical);

        Assert.True(read);
        Assert.Null(canonical);
    }

    /// <summary>Entries are trimmed and written back in the canonical address/prefix spelling, in the order given.</summary>
    [Fact]
    public void Entries_are_written_back_canonically()
    {
        var read = AllowedFromPolicy.TryRead(" 10.0.0.1 ,192.168.0.0/24, 2001:db8::/32", out var canonical);

        Assert.True(read);
        Assert.Equal("10.0.0.1/32,192.168.0.0/24,2001:db8::/32", canonical);
    }

    /// <summary>One bad entry refuses the whole list and leaves nothing to install; the same list without it is read.</summary>
    [Fact]
    public void One_bad_entry_refuses_the_whole_list()
    {
        var refused = AllowedFromPolicy.TryRead("10.0.0.1,evil\"", out var none);
        var accepted = AllowedFromPolicy.TryRead("10.0.0.1", out var canonical);

        Assert.False(refused);
        Assert.Null(none);
        Assert.True(accepted);
        Assert.Equal("10.0.0.1/32", canonical);
    }

    /// <summary>A text the validator would have refused is not quietly read as no restriction; a valid one is read as itself.</summary>
    [Fact]
    public void A_text_that_cannot_be_read_is_refused_loudly_and_never_read_as_no_restriction()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return AllowedFromPolicy.Require("10.0.0.1,evil\"");
        });
        Assert.Null(AllowedFromPolicy.Require(null));
        Assert.Equal("10.0.0.1/32", AllowedFromPolicy.Require("10.0.0.1"));
    }
}
