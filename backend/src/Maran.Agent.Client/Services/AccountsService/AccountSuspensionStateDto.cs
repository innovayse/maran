namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>What the host can be observed to be doing for an account right now.</summary>
/// <remarks>
/// <para>
/// Read-only, and it exists because suspension's residue is on the HOST — a vhost still serving, a
/// login still unlocked — and not in the panel's own rows. The database-backed residue audit that
/// verifies a deletion cannot verify this: a check that reported on the host by reading the panel's
/// tables would be green over a serving site.
/// </para>
/// <para>
/// <b>What it does NOT observe.</b> The account's databases, which keep accepting connections, and
/// the panel's own web login, which is not on this host — both open product decisions rather than
/// oversights. The crontab's FOREIGN lines are not suspended either, and are counted in
/// <paramref name="CronForeignLines"/> rather than left out: a suspension that said nothing about
/// them would be claiming a silence it did not achieve.
/// </para>
/// </remarks>
/// <param name="LoginPasswordState">
/// What the account's own shadow password field holds, as the agent classified it. This is the
/// account's own login only; the file-transfer logins of both daemons are separate passwd entries
/// with their own facts in <paramref name="FileTransferLogins"/>. <c>passwd -S</c>, the source of the
/// wire's older <c>login_locked</c> boolean, reports a login locked over a password and one that
/// never had a password as the same thing, and every hosting account is the second kind, so a
/// boolean is true for such an account for ever; the four-state value is the fact both directions
/// can be asked about. A caller asking whether a suspension took hold and one asking whether a
/// reactivation completed read the same value through different questions. An agent that predates
/// the classification sends only the boolean: <see cref="AgentAccountsClient"/> turns that into
/// <see cref="AccountLoginPasswordState.Locked"/> or <see cref="AccountLoginPasswordState.Usable"/>
/// at the boundary, which is the conservative reading in both directions (suspension and
/// reactivation both refuse on a lock), so this member is always one of the four.
/// </param>
/// <param name="SitesDirectoryReadable">
/// <c>true</c> when the agent could read the directory it enumerates vhosts from. This is the field
/// that tells "the account has no vhost" apart from "the agent could not look", which produce the
/// same empty <paramref name="Sites"/> — and the empty list is the one that reads as "everything is
/// suspended". A caller MUST refuse a suspension when this is <c>false</c>.
/// </param>
/// <param name="Sites">One fact per vhost the host serves for the account; empty is a real answer.</param>
/// <param name="CronEntriesTotal">
/// How many entries the panel manages in the account's crontab, counted out of the crontab itself.
/// The Cron module keeps no rows at all, so this is the only place the answer exists: a check that
/// asked the database would be green over a firing crontab.
/// </param>
/// <param name="CronEntriesSuspended">
/// How many of those carry the suspension marker on their installed line. Read off the LINE, which
/// is what decides whether cron can see the schedule. An entry the CUSTOMER disabled themselves
/// counts as unsuspended here, because resuming must give that entry back exactly as they left it.
/// Both counts zero is what an account with no managed entries looks like, and that is a suspended
/// crontab: a crontab the agent could not read is a failure of the call, never an empty count.
/// </param>
/// <param name="CronForeignLines">
/// How many lines the account's crontab holds that the panel did not write. Suspension does not
/// touch them — a crontab is not the agent's file — so they keep firing under a suspended account.
/// Reported so the panel can say so; it does not refuse on them.
/// </param>
/// <param name="FileTransferLogins">
/// One fact per <c>&lt;account&gt;_*</c> login the HOST's password database holds, whether or not
/// the panel remembers creating it. Empty is a real answer; a database that could not be
/// enumerated is a failure of the call. Since FTPS shipped this list holds logins of BOTH transfer
/// daemons, told apart by <see cref="FileTransferLoginSuspensionFactDto.Protocol"/>.
///
/// <b>This member is named for the pair; the wire field it comes from is not.</b> It is filled
/// from the response's <c>sftp_logins</c>, which keeps its name and number under the additive law
/// while this layer spells what it actually holds. The mapping is the one place the two spellings
/// meet, and it says so where it happens
/// (<see cref="AgentAccountsClient.GetSuspensionStateAsync"/>).
/// </param>
/// <param name="UnmanagedLogins">
/// How many passwd entries share this account's uid WITHOUT being one of its jailed logins — an
/// operator's own <c>useradd --non-unique</c>, or a login of a neighbouring jail that happens to
/// share the uid. The agent locks none of them: they are not its entries, and turning a credential
/// nobody asked it to touch would take away access somebody deliberately arranged.
///
/// This is the part of the attestation that says what the suspension did NOT cover, which is the
/// only part of an attestation that can be wrong in the dangerous direction. The enumeration behind
/// <paramref name="FileTransferLogins"/> selects by JAIL, so this is precisely the number it cannot speak
/// for, and a panel that showed the covered count while swallowing this one would be reporting a
/// silence the agent never claimed. It is REPORTED and never refused on — the precedent
/// <paramref name="CronForeignLines"/> set for this exact shape: the panel shows the number and the
/// operator decides.
///
/// A count and not a list, deliberately: the panel's question is whether the attestation covered
/// everything, and a name here would be a name the panel had no business learning from a credential
/// it does not own.
///
/// It is a type and not a number because the wire cannot tell "none exist" from "the agent was too
/// old to look": <see cref="UnmanagedLoginCount"/> carries the decision, made once at the boundary
/// where the raw answer is in hand, so no caller can read an absent count as zero.
/// </param>
public sealed record AccountSuspensionStateDto(
    AccountLoginPasswordState LoginPasswordState,
    bool SitesDirectoryReadable,
    IReadOnlyList<SiteSuspensionFactDto> Sites,
    uint CronEntriesTotal,
    uint CronEntriesSuspended,
    uint CronForeignLines,
    IReadOnlyList<FileTransferLoginSuspensionFactDto> FileTransferLogins,
    UnmanagedLoginCount UnmanagedLogins);
