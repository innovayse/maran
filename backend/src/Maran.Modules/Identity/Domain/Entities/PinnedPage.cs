using Maran.Modules.Identity.Domain.Policies;

namespace Maran.Modules.Identity.Domain.Entities;

/// <summary>
/// One page a user pinned to the app bar: the key of an item in the SPA's navigation tree, kept for
/// that user alone. The server stores the key and nothing about the page — what is drawn, and
/// whether the caller is still offered it, is the SPA's decision.
/// </summary>
public sealed class PinnedPage
{
    /// <summary>The pin's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The user the pin belongs to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The navigation tree key of the pinned page; see <see cref="PinnedPagePolicy.IsValidItemKey"/>.</summary>
    public string ItemKey { get; private set; }

    /// <summary>Where the pin sits in the user's list, lowest first.</summary>
    public int Position { get; private set; }

    /// <summary>The instant the page was pinned.</summary>
    public DateTimeOffset PinnedAt { get; private set; }

    /// <summary>Pins a page for a user.</summary>
    /// <param name="id">The pin's identity.</param>
    /// <param name="userId">The user the pin belongs to.</param>
    /// <param name="itemKey">The navigation tree key; must satisfy <see cref="PinnedPagePolicy.IsValidItemKey"/>.</param>
    /// <param name="position">Where the pin sits, from <see cref="PinnedPagePolicy.NextPosition"/>.</param>
    /// <param name="at">The instant of pinning, taken from <see cref="IClock"/>.</param>
    /// <exception cref="ArgumentException">The key does not have the shape of a navigation key.</exception>
    public PinnedPage(Guid id, Guid userId, string itemKey, int position, DateTimeOffset at)
    {
        if (!PinnedPagePolicy.IsValidItemKey(itemKey))
        {
            throw new ArgumentException("Not a navigation key.", nameof(itemKey));
        }

        Id = id;
        UserId = userId;
        ItemKey = itemKey;
        Position = position;
        PinnedAt = at;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private PinnedPage()
    {
        ItemKey = string.Empty;
    }
}
