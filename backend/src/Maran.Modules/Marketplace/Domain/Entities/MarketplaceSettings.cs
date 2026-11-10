namespace Maran.Modules.Marketplace.Domain.Entities;

/// <summary>
/// Whether this installation may talk to the marketplace at all. Exactly one row exists, addressed by
/// <see cref="SingletonId"/>, and a server that never switched the marketplace on has no row and reads
/// as off.
/// </summary>
/// <remarks>
/// <para>
/// <b>Off by default, and a database row rather than a setting.</b> The switch is what stands between the
/// panel and its second outbound destination (rules/security.md item 10). A configuration key or an
/// environment variable would let a typo in <c>panel.env</c> turn the feature on with no administrator
/// and no audit entry; a row is changed only through <see cref="Enable"/> and <see cref="Disable"/>,
/// each of which the caller audits.
/// </para>
/// <para>
/// <b>Not tenant-scoped.</b> The row belongs to the installation, carries no <c>AccountId</c>, and every
/// path to it is administrator-only.
/// </para>
/// </remarks>
public sealed class MarketplaceSettings
{
    /// <summary>The identity of the single row, so a second one cannot be inserted.</summary>
    public static readonly Guid SingletonId = new("8c1b7f0e-5d3a-4e62-9b4f-2a6d1c9e7a30");

    /// <summary>The row's identity; always <see cref="SingletonId"/>.</summary>
    public Guid Id { get; private set; }

    /// <summary>Whether the marketplace is switched on. False until an administrator enables it.</summary>
    public bool Enabled { get; private set; }

    /// <summary>When the switch last changed, or null when it never has.</summary>
    public DateTimeOffset? ChangedAt { get; private set; }

    /// <summary>The administrator who last changed the switch, or null when it never changed.</summary>
    public Guid? ChangedBy { get; private set; }

    /// <summary>Parameterless constructor required by EF Core materialization; it builds the off state.</summary>
    private MarketplaceSettings()
    {
        Id = SingletonId;
    }

    /// <summary>The state of a server whose switch was never touched: off, changed by nobody, never.</summary>
    /// <returns>A new, unsaved settings object that reads as off.</returns>
    public static MarketplaceSettings Off()
    {
        return new MarketplaceSettings();
    }

    /// <summary>Switches the marketplace on.</summary>
    /// <param name="at">The instant of the change.</param>
    /// <param name="by">The administrator who did it.</param>
    public void Enable(DateTimeOffset at, Guid by)
    {
        Enabled = true;
        ChangedAt = at;
        ChangedBy = by;
    }

    /// <summary>Switches the marketplace off. Installed modules keep running; only the outbound path closes.</summary>
    /// <param name="at">The instant of the change.</param>
    /// <param name="by">The administrator who did it.</param>
    public void Disable(DateTimeOffset at, Guid by)
    {
        Enabled = false;
        ChangedAt = at;
        ChangedBy = by;
    }
}
