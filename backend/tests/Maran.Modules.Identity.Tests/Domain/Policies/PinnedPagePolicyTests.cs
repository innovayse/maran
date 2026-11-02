using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;

namespace Maran.Modules.Identity.Tests.Domain.Policies;

/// <summary>Behavioural contract of the rules about a user's pinned pages.</summary>
public sealed class PinnedPagePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static List<PinnedPage> Pins(Guid user, int count)
    {
        return Enumerable.Range(0, count)
            .Select(index =>
            {
                return new PinnedPage(Guid.NewGuid(), user, $"page-{index}", index, Now);
            })
            .ToList();
    }

    /// <summary>Keys shaped like the navigation tree's are accepted.</summary>
    [Theory]
    [InlineData("dashboard")]
    [InlineData("accounts-list")]
    [InlineData("p2")]
    public void A_tree_shaped_key_is_valid(string key)
    {
        Assert.True(PinnedPagePolicy.IsValidItemKey(key));
    }

    /// <summary>Anything else is refused, control characters and over-long keys included.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Accounts")]
    [InlineData("1abc")]
    [InlineData("a b")]
    [InlineData("a/b")]
    [InlineData("a\nb")]
    [InlineData("a_b")]
    public void A_key_outside_the_tree_charset_is_invalid(string? key)
    {
        Assert.False(PinnedPagePolicy.IsValidItemKey(key));
    }

    /// <summary>The length ceiling is inclusive.</summary>
    [Fact]
    public void A_key_is_valid_up_to_the_ceiling_and_invalid_beyond_it()
    {
        Assert.True(PinnedPagePolicy.IsValidItemKey(new string('a', PinnedPagePolicy.MaxItemKeyLength)));
        Assert.False(PinnedPagePolicy.IsValidItemKey(new string('a', PinnedPagePolicy.MaxItemKeyLength + 1)));
    }

    /// <summary>A user with room and no duplicate is admitted.</summary>
    [Fact]
    public void A_new_page_is_admitted_while_there_is_room()
    {
        var pins = Pins(Guid.NewGuid(), PinnedPagePolicy.MaxPinsPerUser - 1);

        Assert.Equal(PinAdmission.Admitted, PinnedPagePolicy.Admit(pins, "fresh"));
    }

    /// <summary>The limit refuses the page after the last allowed one.</summary>
    [Fact]
    public void A_user_at_the_limit_is_refused()
    {
        var pins = Pins(Guid.NewGuid(), PinnedPagePolicy.MaxPinsPerUser);

        Assert.Equal(PinAdmission.LimitReached, PinnedPagePolicy.Admit(pins, "fresh"));
    }

    /// <summary>A duplicate is named as one even when the user is also at the limit.</summary>
    [Fact]
    public void A_duplicate_is_reported_ahead_of_the_limit()
    {
        var pins = Pins(Guid.NewGuid(), PinnedPagePolicy.MaxPinsPerUser);

        Assert.Equal(PinAdmission.AlreadyPinned, PinnedPagePolicy.Admit(pins, "page-3"));
    }

    /// <summary>The next position follows the highest held, leaving gaps from unpinning alone.</summary>
    [Fact]
    public void The_next_position_follows_the_highest_one_held()
    {
        var user = Guid.NewGuid();
        var pins = new List<PinnedPage> { new(Guid.NewGuid(), user, "a", 0, Now), new(Guid.NewGuid(), user, "b", 4, Now) };

        Assert.Equal(5, PinnedPagePolicy.NextPosition(pins));
        Assert.Equal(0, PinnedPagePolicy.NextPosition([]));
    }

    /// <summary>An entity cannot be built around a key that is not a navigation key.</summary>
    [Fact]
    public void A_pin_cannot_be_created_with_an_invalid_key()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return new PinnedPage(Guid.NewGuid(), Guid.NewGuid(), "Bad Key", 0, Now);
        });
    }
}
