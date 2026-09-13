namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>What an account's own shadow password field holds, as the agent classified it.</summary>
/// <remarks>
/// <para>
/// This exists because one boolean could not carry two directions. <c>passwd -S</c> — the source of
/// <see cref="AccountSuspensionStateDto.LoginLocked"/> — reports a login LOCKED OVER A PASSWORD and
/// a login that NEVER HAD ONE as the same thing (<c>L</c> on the Debian family, <c>LK</c> on the
/// RHEL one). Every hosting account is the second kind, because the agent sets no password on an
/// account's own entry. So the boolean is true for such an account for ever: before a suspension,
/// during it, and after it.
/// </para>
/// <para>
/// A suspension asks "can a password authenticate this login" and the boolean answers correctly. A
/// reactivation asks "is anything of the customer's still held down", and only this enum can answer
/// that — the raw shadow field makes the distinction, identically on both families.
/// </para>
/// <para>
/// No hash and no field bytes reach here: the agent classifies at the point of reading and drops
/// them, and this type has four inhabitants plus the skew value.
/// </para>
/// </remarks>
public enum AccountLoginPasswordState
{
    /// <summary>
    /// The agent predates this field and said nothing. A caller MUST fall back to
    /// <see cref="AccountSuspensionStateDto.LoginLocked"/>, which is the behaviour that existed
    /// before the field and is conservative in both directions — suspension refuses, reactivation
    /// refuses.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The field is empty: the login authenticates with NO password. A suspension must never leave a
    /// login here, and it is what <c>passwd -u -f</c> produces on the RHEL family — the reason that
    /// command is used nowhere in this product.
    /// </summary>
    Empty = 1,

    /// <summary>
    /// The field holds lock markers only (<c>!</c>, <c>!!</c>, <c>*</c>) and no hash. Nothing can
    /// authenticate against it and there is nothing to restore. This is how every account the agent
    /// creates begins and ends its life, and it is indistinguishable from
    /// <see cref="Locked"/> through <c>passwd -S</c>.
    /// </summary>
    Absent = 2,

    /// <summary>
    /// A real password hash behind a leading <c>!</c>: locked, and reversible. The only state in
    /// which something of the customer's is actually held down.
    /// </summary>
    Locked = 3,

    /// <summary>A real password hash with no lock marker: the login can authenticate.</summary>
    Usable = 4,
}
