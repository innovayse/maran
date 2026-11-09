using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.ValueObjects;

namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>
/// The rules about a person's dashboard layout: what a key looks like, how long a list may be, which
/// widgets and shortcuts the panel offers by default, and how a person's stored choices combine with
/// those defaults into the list the dashboard draws. Stateless, so the validator, the read and the
/// save all use the same numbers.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this validates, and what it deliberately does not.</b> A key is checked for its SHAPE and
/// nothing else. The widgets are drawn by the SPA bundle, so the server cannot know every key a
/// newer or older bundle understands. A key the server holds no default for is stored as sent,
/// returned as stored and simply never drawn by a bundle that cannot draw it; dropping it would
/// erase a choice the person made on a newer panel the moment an older one saved. Which widgets a
/// caller may SEE is not decided here: every widget reads through an endpoint that enforces its own
/// permission, and the layout is only the order and the on/off choice.
/// </para>
/// <para>
/// The ceiling exists to bound a per-person list the dashboard draws in full, not to protect
/// anything.
/// </para>
/// </remarks>
public static class DashboardLayoutPolicy
{
    /// <summary>The most entries one list (widgets, or shortcuts) may hold.</summary>
    public const int MaxEntriesPerList = 32;

    /// <summary>The longest widget or shortcut key accepted.</summary>
    public const int MaxKeyLength = 64;

    /// <summary>The widgets the administrator's dashboard offers, in the order they are drawn until the person rearranges them.</summary>
    private static readonly string[] AdministratorWidgets =
    {
        "attention", "firstRun", "cpu", "memory", "disk", "network", "services", "totals", "activity",
    };

    /// <summary>The shortcuts the administrator's quick-actions bar offers, in the order they are drawn until the person rearranges them.</summary>
    private static readonly string[] AdministratorActions =
    {
        "createAccount", "addSite", "createDatabase", "runBackup", "banAddress", "addCronJob",
    };

    /// <summary>Reports whether a text has the shape of a widget or shortcut key.</summary>
    /// <param name="key">The candidate key.</param>
    /// <returns>
    /// True for an ASCII letter followed by ASCII letters and digits, no longer than
    /// <see cref="MaxKeyLength"/>. Spaces, separators and control characters never qualify.
    /// </returns>
    public static bool IsValidKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength || !char.IsAsciiLetter(key[0]))
        {
            return false;
        }

        return key.All(char.IsAsciiLetterOrDigit);
    }

    /// <summary>Combines a person's stored choices for one list with the panel's defaults.</summary>
    /// <param name="stored">The person's stored preferences; rows of other kinds are ignored.</param>
    /// <param name="kind">Which list to build.</param>
    /// <param name="forAdministrator">
    /// True when the caller is an administrator, whose dashboard has defaults. Any other caller has
    /// none yet, so the list holds only what that person stored.
    /// </param>
    /// <returns>
    /// The stored entries in the person's order, then every default the person has no entry for, in
    /// the default order and not hidden — so a widget a newer panel adds appears at the end instead
    /// of being missing. Keys no default names are kept where the person put them.
    /// </returns>
    public static IReadOnlyList<DashboardLayoutEntry> Effective(
        IEnumerable<DashboardPreference> stored,
        DashboardPreferenceKind kind,
        bool forAdministrator)
    {
        ArgumentNullException.ThrowIfNull(stored);

        var own = stored
            .Where(preference =>
            {
                return preference.Kind == kind;
            })
            .OrderBy(preference =>
            {
                return preference.Position;
            })
            .ThenBy(preference =>
            {
                return preference.Key;
            }, StringComparer.Ordinal)
            .Select(preference =>
            {
                return new DashboardLayoutEntry(preference.Key, preference.Hidden);
            })
            .ToList();

        var held = own.Select(entry =>
        {
            return entry.Key;
        }).ToHashSet(StringComparer.Ordinal);

        foreach (var key in DefaultsFor(kind, forAdministrator))
        {
            if (!held.Contains(key))
            {
                own.Add(new DashboardLayoutEntry(key, Hidden: false));
            }
        }

        return own;
    }

    /// <summary>Names the default keys of one list for an audience.</summary>
    /// <param name="kind">Which list.</param>
    /// <param name="forAdministrator">True for the administrator's dashboard.</param>
    /// <returns>The default keys in drawing order; empty for an audience with no default dashboard.</returns>
    private static string[] DefaultsFor(DashboardPreferenceKind kind, bool forAdministrator)
    {
        if (!forAdministrator)
        {
            return Array.Empty<string>();
        }

        return kind == DashboardPreferenceKind.Widget ? AdministratorWidgets : AdministratorActions;
    }
}
