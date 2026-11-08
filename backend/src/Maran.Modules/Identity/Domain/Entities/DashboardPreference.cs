using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;

namespace Maran.Modules.Identity.Domain.Entities;

/// <summary>
/// One entry of a person's own dashboard layout: a widget or a shortcut, where the person put it and
/// whether they turned it off. Kept for that person alone and never for an account, so it follows the
/// login whichever account the login acts for, and is erased with the login. The server stores the key
/// and nothing about what it draws; whether the SPA knows the key is the SPA's concern.
/// </summary>
public sealed class DashboardPreference
{
    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The user the preference belongs to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Whether the entry is a widget or a shortcut.</summary>
    public DashboardPreferenceKind Kind { get; private set; }

    /// <summary>The key of the widget or shortcut; see <see cref="DashboardLayoutPolicy.IsValidKey"/>.</summary>
    public string Key { get; private set; }

    /// <summary>Where the entry sits in its list, lowest first.</summary>
    public int Position { get; private set; }

    /// <summary>True when the person turned the entry off.</summary>
    public bool Hidden { get; private set; }

    /// <summary>Records an entry of a user's layout.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="userId">The user the preference belongs to.</param>
    /// <param name="kind">The list the entry belongs to.</param>
    /// <param name="key">The widget or shortcut key; must satisfy <see cref="DashboardLayoutPolicy.IsValidKey"/>.</param>
    /// <param name="position">Where the entry sits in its list.</param>
    /// <param name="hidden">Whether the person turned the entry off.</param>
    /// <exception cref="ArgumentException">The key does not have the shape of a dashboard key.</exception>
    public DashboardPreference(Guid id, Guid userId, DashboardPreferenceKind kind, string key, int position, bool hidden)
    {
        if (!DashboardLayoutPolicy.IsValidKey(key))
        {
            throw new ArgumentException("Not a dashboard key.", nameof(key));
        }

        Id = id;
        UserId = userId;
        Kind = kind;
        Key = key;
        Position = position;
        Hidden = hidden;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private DashboardPreference()
    {
        Key = string.Empty;
    }

    /// <summary>Moves the entry within its list and sets whether it is turned off.</summary>
    /// <param name="position">Where the entry now sits.</param>
    /// <param name="hidden">Whether the person turned the entry off.</param>
    public void Place(int position, bool hidden)
    {
        Position = position;
        Hidden = hidden;
    }
}
