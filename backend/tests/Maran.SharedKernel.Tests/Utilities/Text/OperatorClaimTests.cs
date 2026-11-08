using Maran.SharedKernel.Utilities.Text;

namespace Maran.SharedKernel.Tests.Utilities.Text;

/// <summary>How an operator's claimed name is tidied before it reaches a log-like column.</summary>
public sealed class OperatorClaimTests
{
    /// <summary>An ordinary login name is kept as it is.</summary>
    [Fact]
    public void An_ordinary_login_name_is_kept_as_it_is()
    {
        Assert.Equal("alice", OperatorClaim.Tidy("alice"));
    }

    /// <summary>Control characters are dropped so a claim cannot start a second log line.</summary>
    [Fact]
    public void Control_characters_are_dropped_so_a_claim_cannot_start_a_second_log_line()
    {
        Assert.Equal("alicefake", OperatorClaim.Tidy("alice\r\nfake"));
    }

    /// <summary>A claim longer than the bound is cut to it.</summary>
    [Fact]
    public void A_claim_longer_than_the_bound_is_cut_to_it()
    {
        Assert.Equal(OperatorClaim.MaxLength, OperatorClaim.Tidy(new string('a', OperatorClaim.MaxLength + 30)).Length);
    }

    /// <summary>A claim with nothing usable in it becomes the stated placeholder.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public void A_claim_with_nothing_usable_in_it_becomes_the_stated_placeholder(string claim)
    {
        Assert.Equal(OperatorClaim.Unknown, OperatorClaim.Tidy(claim));
    }
}
