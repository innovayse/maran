using Maran.Sdk.Contracts;

namespace Maran.Sdk.Tests.Contracts;

/// <summary>Covers <see cref="NotificationDeepLinks"/>: the closed list of destinations, extended deliberately.</summary>
public sealed class NotificationDeepLinksTests
{
    /// <summary>The licence page is a destination, spelled as the SPA's route name; a near miss and an address are not.</summary>
    [Fact]
    public void The_licence_page_is_a_known_destination_and_near_misses_are_not()
    {
        Assert.Equal("licence", NotificationDeepLinks.Licence);
        Assert.Contains(NotificationDeepLinks.Licence, NotificationDeepLinks.All);
        Assert.True(NotificationDeepLinks.IsKnown("licence"));
        Assert.False(NotificationDeepLinks.IsKnown("Licence"));
        Assert.False(NotificationDeepLinks.IsKnown("https://example.test/licence"));
        Assert.False(NotificationDeepLinks.IsKnown(null));
    }

    /// <summary>Every destination is distinct, so a name leads to exactly one page.</summary>
    [Fact]
    public void Every_destination_is_listed_once()
    {
        Assert.Equal(NotificationDeepLinks.All.Count, NotificationDeepLinks.All.Distinct(StringComparer.Ordinal).Count());
    }
}
