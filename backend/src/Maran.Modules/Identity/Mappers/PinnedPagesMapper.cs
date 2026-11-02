using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;

namespace Maran.Modules.Identity.Mappers;

/// <summary>
/// Builds the <see cref="PinnedPagesDto"/> the three pinned-page operations all answer with, so a
/// pin, an unpin and a read can never disagree about what the list looks like.
/// </summary>
public static class PinnedPagesMapper
{
    /// <summary>Projects a user's pins into their outward view.</summary>
    /// <param name="pins">The user's pins, in any order.</param>
    /// <returns>The pins ordered by position, with the panel's limit beside them.</returns>
    public static PinnedPagesDto From(IEnumerable<PinnedPage> pins)
    {
        ArgumentNullException.ThrowIfNull(pins);

        var items = pins
            .OrderBy(pin => pin.Position)
            .ThenBy(pin => pin.ItemKey, StringComparer.Ordinal)
            .Select(pin => new PinnedPageDto(pin.ItemKey, pin.Position))
            .ToList();

        return new PinnedPagesDto(items, PinnedPagePolicy.MaxPinsPerUser);
    }
}
