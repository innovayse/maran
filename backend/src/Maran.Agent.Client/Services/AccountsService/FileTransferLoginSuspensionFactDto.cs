namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>What the host can be observed to hold for one of an account's jailed transfer logins.</summary>
/// <remarks>
/// <para>
/// One entry per <c>&lt;account&gt;_*</c> passwd entry the HOST holds, asked of the password
/// database rather than taken from the panel's rows: a login the panel has forgotten is exactly the
/// one still letting a suspended customer in.
/// </para>
/// <para>
/// <b>The name says file transfer; the wire still says SFTP.</b> Since FTPS shipped this record
/// carries logins of BOTH daemons, told apart by <paramref name="Protocol"/> and by nothing else,
/// which is why it is named for the pair rather than for one of them. The wire's own message keeps
/// the name it was born with — <c>SftpLoginSuspensionFact</c>, carried in <c>sftp_logins</c> —
/// because a message on the contract is renamed by nothing (rules/architecture.md, additive
/// evolution). The two vocabularies meet in
/// <see cref="AgentAccountsClient.GetSuspensionStateAsync"/> and nowhere else; a type name stating
/// one protocol over a list holding two is exactly how the sentence on the suspension screen came
/// to say "sftp logins" about FTPS credentials.
/// </para>
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
/// <param name="Protocol">
/// Which daemon serves this login, as the agent DERIVED it from the jail the login's passwd home
/// sits under. Never inferred from the name: <c>alice_web</c> is a valid login under either daemon.
/// <see cref="LoginTransferProtocol.Unspecified"/> means the agent predates the field, and such a
/// login is read as <see cref="LoginTransferProtocol.Sftp"/> — before FTPS existed every reported
/// login was an SFTP one by construction.
///
/// It carries a default so that this field could be added without editing the Accounts module and
/// its tests, which construct this record and are not this change's to touch. The default is the
/// wire's own absent value, so a caller that does not pass one reads exactly what an old agent
/// would have sent — not a value this record invented.
/// </param>
public sealed record FileTransferLoginSuspensionFactDto(
    string Username,
    bool Locked,
    LoginTransferProtocol Protocol = LoginTransferProtocol.Unspecified);
