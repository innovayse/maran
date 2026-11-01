namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>
/// Whether one ceiling of an account's systemd slice is set. Mirrors the two cases of the wire's
/// <c>ResourceCeiling</c>, so "no ceiling" is stated rather than read off a zero.
/// </summary>
public enum SliceCeilingKind
{
    /// <summary>The slice is held to <see cref="SliceCeiling.Maximum"/>.</summary>
    Limited,

    /// <summary>The slice has no ceiling for this resource.</summary>
    Unlimited,
}
