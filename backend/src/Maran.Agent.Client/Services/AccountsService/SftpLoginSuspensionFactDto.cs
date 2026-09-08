namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>What the host can be observed to hold for one of an account's SFTP logins.</summary>
/// <remarks>
/// One entry per <c>&lt;account&gt;_*</c> passwd entry the HOST holds, asked of the password
/// database rather than taken from the panel's rows: a login the panel has forgotten is exactly the
/// one still letting a suspended customer in.
/// </remarks>
/// <param name="Username">The login's full system name, as the password database spells it.</param>
/// <param name="Locked">
/// <c>true</c> when that passwd entry's password is locked, as <c>passwd -S</c> reports it. Observed
/// per login and never inferred from the account's own lock — that inference is the defect this
/// record exists to detect, because <c>usermod --lock &lt;account&gt;</c> reaches none of these
/// entries. One honest exception: a login that never had a password cannot be unlocked, so it reads
/// locked after a resume; every login the agent creates is given one, so such a login was made by
/// hand.
/// </param>
public sealed record SftpLoginSuspensionFactDto(string Username, bool Locked);
