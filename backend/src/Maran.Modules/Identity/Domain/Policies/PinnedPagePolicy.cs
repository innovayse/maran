using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;

namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>
/// The rules about a user's pinned pages: how many there may be, what a navigation key looks like,
/// and where the next one goes. Stateless, so the handlers, the validators and the DTO that tells
/// the SPA the limit all read the same numbers.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this validates, and what it deliberately does not.</b> It checks the SHAPE of a key — the
/// characters the SPA's navigation tree uses and a length — and nothing else. The tree lives in the
/// SPA bundle, so the server cannot know whether a key names a page that exists, is built, or is
/// offered to this caller's role, module or plan. A key naming nothing is therefore stored as sent
/// and simply never drawn; a page that is planned has no pin control in the SPA, but the server does
/// not refuse its key. Both are display decisions, and the enforcement boundary for a page is the
/// endpoint behind it, never a pin.
/// </para>
/// <para>
/// The ceiling exists to bound a per-user list that the app bar draws in full, not to protect
/// anything: the SPA reads it from the list response instead of holding its own copy.
/// </para>
/// </remarks>
public static class PinnedPagePolicy
{
    /// <summary>The most pages one user may keep pinned.</summary>
    public const int MaxPinsPerUser = 8;

    /// <summary>The longest navigation key accepted.</summary>
    public const int MaxItemKeyLength = 64;

    /// <summary>Reports whether a text has the shape of a navigation tree key.</summary>
    /// <param name="itemKey">The candidate key.</param>
    /// <returns>
    /// True for a lowercase ASCII letter followed by lowercase letters, digits and hyphens, no longer
    /// than <see cref="MaxItemKeyLength"/>. Spaces, separators and control characters never qualify.
    /// </returns>
    public static bool IsValidItemKey(string? itemKey)
    {
        if (string.IsNullOrEmpty(itemKey) || itemKey.Length > MaxItemKeyLength)
        {
            return false;
        }

        if (itemKey[0] is < 'a' or > 'z')
        {
            return false;
        }

        foreach (var character in itemKey)
        {
            var allowed = character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Decides whether a user holding <paramref name="existing"/> may pin one more page.</summary>
    /// <param name="existing">The pins the user already holds.</param>
    /// <param name="itemKey">The key of the page to pin, already shape-checked.</param>
    /// <returns>The verdict; a duplicate is reported ahead of the limit.</returns>
    public static PinAdmission Admit(IReadOnlyCollection<PinnedPage> existing, string itemKey)
    {
        ArgumentNullException.ThrowIfNull(existing);

        if (existing.Any(pin =>
        {
            return pin.ItemKey == itemKey;
        }))
        {
            return PinAdmission.AlreadyPinned;
        }

        return existing.Count >= MaxPinsPerUser ? PinAdmission.LimitReached : PinAdmission.Admitted;
    }

    /// <summary>Gives the position a newly pinned page takes: after every pin the user holds.</summary>
    /// <param name="existing">The pins the user already holds.</param>
    /// <returns>One past the highest position held, or zero for a first pin.</returns>
    public static int NextPosition(IReadOnlyCollection<PinnedPage> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);

        return existing.Count == 0 ? 0 : existing.Max(pin =>
        {
            return pin.Position;
        }) + 1;
    }
}
