using Maran.Agent.Client.Resources;

namespace Maran.Agent.Client.Services.SystemService;

/// <summary>
/// The contract revisions of the agent this panel drives: the one statement of the window the
/// handshake's <c>proto_version</c> is compared against (<c>proto/agent/v1/system.proto</c>,
/// <c>GetAgentInfo</c>). <see cref="AgentSystemClient"/> is the one place the comparison is made.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a revision is.</b> The agent's <c>PROTO_VERSION</c>
/// (<c>agent/crates/agent/src/services/system/system_service.rs</c>) names the revision of
/// <c>proto/agent/v1/</c> the binary was built from. The policy that it is raised by every change to
/// the contract that <c>maran proto --accept</c> records, and that this window moves in the same
/// change, is rules/proto.md "The contract revision". Revision 1 is every agent built before that
/// policy existed: the constant stayed at 1 through every revision, so a 1 says nothing about which
/// contract the agent speaks, and it is outside this window for exactly that reason. Revision 2 is
/// the contract as of the change that introduced the policy (issue #88). Revision 3 adds
/// <c>InstallCertificate</c>'s <c>refused</c> answer (issue #214), and both ends moved to it: a
/// revision-2 agent would answer a refused pair with a bare <c>InvalidInput</c>, and the panel
/// could no longer tell the customer which half to fix. A 0 is what protobuf reads
/// from a sender that does not set the field at all, and it is outside the window too. Revision 4
/// adds the two package-source failure codes (issue #251), and both ends moved to it as the
/// one-revision default: a revision-3 agent reports a mirror that refused or could not be reached as
/// a bare <c>SystemFailure</c>, so the panel would answer a fault outside the host as a 500 of its own.
/// Revision 5 adds <c>GetAccountsDiskUsageRequest.account_usernames</c> (issue #196), and both ends
/// moved to it as the one-revision default: a revision-4 agent ignores the field and walks every
/// account's home on each request for one page, which is the unbounded walk the field exists to end.
/// Revision 6 splits the root filesystem into used, reserved and available
/// (<c>HostMetrics.disk_reserved_bytes</c>, <c>disk_available_bytes</c>) and redefines
/// <c>disk_used_bytes</c> as <c>df</c>'s <c>Used</c> where it was total minus available (issue #77).
/// Both ends moved to it, and here the one-revision default is not a preference but the guarantee:
/// a revision-5 agent sends no reserve and no available figure, which protobuf reads as zero, and
/// sends a used figure with the reserve folded in — so a panel driving one would report a full disk
/// as having nothing left reserved and nothing available, and a field read under the wrong revision
/// would mean two things.
/// Revision 7 adds <c>ERROR_CODE_BELOW_FREE_SPACE_FLOOR</c> and
/// <c>HostMetrics.free_space_floor_bytes</c> (issue #148), and both ends moved to it as the
/// one-revision default: a revision-6 agent never refuses at a floor and sends no floor, which
/// protobuf reads as zero, so the panel's "approaching the floor" alert could never fire against it.
/// Revision 8 adds <c>AccountsService.SetAccountResourceLimits</c> (issue #133), and both ends moved
/// to it: the panel calls that rpc whenever an account is created or its plan's ceilings change, and
/// a revision-7 agent answers it <c>UNIMPLEMENTED</c>, so every account creation would fail.
/// Revision 9 adds <c>InstallPanelCertificate</c>, <c>WritePanelChallenge</c> and
/// <c>RemovePanelChallenge</c> (issue #132), and both ends moved to it: an older agent answers
/// the three rpcs <c>UNIMPLEMENTED</c>, so the panel's own certificate could never be issued or
/// renewed against it.
/// Revision 10 adds <c>MonitorService.GetClockSync</c> (issue #150), and both ends moved to it as the
/// one-revision default: a revision-9 agent answers it <c>UNIMPLEMENTED</c>, so the panel would report
/// the clock as unchecked on every round and never warn about one that drifted.
/// Revision 11 adds <c>CronService.RunCronEntry</c> and <c>ERROR_CODE_ACCOUNT_SUSPENDED</c>
/// (issue #158), and both ends moved to it again: an older agent answers the rpc <c>UNIMPLEMENTED</c>, so
/// "Run now" could never succeed against it.
/// Revision 12 adds <c>SftpService.SetSftpAuthorizedKeys</c> (issue #135), and both ends moved to it:
/// an older agent answers the rpc <c>UNIMPLEMENTED</c>, so an SFTP login's public keys could never be
/// written against it.
/// Revision 13 replaces three boolean answers with states (issue #323): the FTPS TLS enforcement
/// (<c>tls_enforcement</c>), whether a suspended site serves the stub (<c>SiteSuspensionFact.serving</c>),
/// and a ban's term (<c>BanAddressRequest.term</c>), and reserves four fields nobody read. Both ends
/// moved to it as the one-revision default: a revision-12 agent sends none of the three new fields,
/// which protobuf reads as the unset zero, so the panel would have to guess at the answer it was
/// handed, and a revision-12 agent ignores the ban term and would turn a timed ban into a permanent one.
/// Revision 14 adds <c>PhpService.ListInstallablePhpVersions</c> (issue #330), and both ends moved to
/// it: an older agent answers the rpc <c>UNIMPLEMENTED</c>, so the operator's PHP install picker could
/// never be filled against it.
/// Revision 15 adds <c>SftpService.EndSftpSessions</c> and <c>ERROR_CODE_AGENT_SATURATED</c>
/// (issues #339 and #333), and both ends moved to it: a revision-14 agent answers the rpc
/// <c>UNIMPLEMENTED</c>, so a revoked SFTP key could never cut the sessions it opened, and it admits
/// any number of cron runs at once, so the panel's own bound on them would be the only one.
/// Revision 16 adds <c>ERROR_CODE_RUN_NOT_STOPPED</c> (issue #336), and both ends moved to it: a
/// revision-15 agent reports a run whose unit it could not confirm stopped as a bare
/// <c>SystemFailure</c>, which the panel would answer with a sentence saying nothing was altered
/// over a job that may still be running.
/// Revision 17 adds <c>MonitorService.ListPendingUpdates</c> (issue #324), and both ends moved to it:
/// a revision-16 agent answers the rpc <c>UNIMPLEMENTED</c>, so the Updates page could never be filled
/// and the navigation badge could never be fed against it.
/// Revision 18 adds <c>BackupService.VerifyBackup</c> (issue #134), and both ends moved to it: a
/// revision-17 agent answers the rpc <c>UNIMPLEMENTED</c>, so a backup could never be verified after
/// its run and a corrupt artifact would go unnoticed until someone needed to restore from it.
/// </para>
/// <para>
/// <b>Both ends are needed, and they move for different reasons.</b> An agent NEWER than
/// <see cref="Maximum"/> may have changed what a field means in a way this panel cannot know about.
/// An agent OLDER than <see cref="Minimum"/> lacks fields this panel reads, and protobuf turns an
/// absent field into its zero value without a word, so the panel would act on a zero it never
/// received (<c>Maran.Modules.Accounts.Domain.Policies.UnmanagedLoginPolicy</c> is the documented
/// case of a module reasoning around exactly that). <see cref="Maximum"/> is the revision this panel
/// was built against and moves with every bump; <see cref="Minimum"/> moves with it unless the change
/// that bumps it states, here, why the panel reads nothing the older revision lacks.
/// </para>
/// <para>
/// <b>What a one-revision window refuses, and for how long.</b> Panel and agent ship together, so a
/// pair outside the window is an upgrade that replaced one side and not the other. The installer
/// restarts the agent before the panel (<c>installer/lib/70-services.sh</c>), so on that path the
/// panel's first handshake already meets the new agent. An update that restarts the panel first —
/// the order the design spec plans for <c>maran update</c> — is refused for the seconds between the
/// two restarts, and that refusal ends by itself: the handshake is asked again on every health and
/// readiness request and its verdict is never cached, so the API needs no restart to see the new
/// agent. That is deliberate rather than tolerated: in those seconds the panel IS talking to an agent
/// whose answers it may misread.
/// </para>
/// </remarks>
public static class AgentProtoVersionWindow
{
    /// <summary>The oldest agent contract revision this panel drives.</summary>
    public const uint Minimum = 18;

    /// <summary>The newest agent contract revision this panel drives: the one it was built against.</summary>
    public const uint Maximum = 18;

    /// <summary>
    /// The machine code a handshake outside the window fails with, so a caller outside this assembly
    /// (the health probe) can tell "answered in a contract this panel does not drive" from "did not
    /// answer" without restating the spelling.
    /// </summary>
    public const string IncompatibleCode = nameof(ErrorMessages.AgentIncompatible);

    /// <summary>Whether an agent reporting <paramref name="protoVersion"/> may be driven by this panel.</summary>
    /// <param name="protoVersion">The revision the agent stated in its handshake.</param>
    /// <returns><c>true</c> when the revision lies inside the window, both ends included.</returns>
    public static bool Contains(uint protoVersion)
    {
        return protoVersion is >= Minimum and <= Maximum;
    }
}
