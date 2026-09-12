using Maran.Agent.Client.Services.AccountsService;

namespace Maran.Modules.Accounts.Domain.Policies;

/// <summary>
/// What a suspension attestation may say about the logins that share the account's uid and that the
/// panel neither created nor locks.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The host reports, per account, how many passwd entries share the
/// account's uid without being one of its jailed logins — an operator's own
/// <c>useradd --non-unique</c>, or a login of a neighbouring jail that happens to share the uid. The
/// agent locks none of them, so a suspension can be completely honest about everything it did and
/// still leave a working credential on the machine. That number is the only part of the attestation
/// that can be wrong in the dangerous direction, and the threat note covering this work
/// (<c>docs/superpowers/notes/2026-09-09-sftp-password-suspension-threat-note.md</c>) asks a reviewer
/// to be sure the panel SHOWS it rather than swallowing it.
/// </para>
/// <para>
/// <b>Three facts, three sentences, and the reason none of them may be collapsed.</b> A count of two
/// is a warning. A count of zero is a completeness claim — the panel looked and there was nothing to
/// report. An UNKNOWN count is neither, and rendering it as zero would put the completeness claim on
/// the operator's screen over an answer the host never gave. That is precisely the shape of the
/// defect this whole attestation exists to end, one field further along.
/// </para>
/// <para>
/// <b>How "unknown" is recognised, and why it is an inference.</b> The wire carries a
/// <c>uint32</c>, so an agent that predates the field is indistinguishable from one reporting zero
/// by looking at the number alone. It is distinguishable by looking at the rest of the same answer:
/// <c>unmanaged_logins</c> and <see cref="FileTransferLoginSuspensionFactDto.Protocol"/> were added to the
/// contract by one change, so an agent that states a protocol for any login has also counted these
/// entries. A non-zero count is self-evidently a measurement; a zero count is one only when some
/// login in the same answer carries a stated protocol.
/// </para>
/// <para>
/// The inference errs in one direction only, deliberately: an account with NO logins at all, or with
/// logins an old agent reported, reads as unknown even when a current agent measured and found none.
/// The panel then says less than it knows, which is the direction an attestation is allowed to be
/// wrong in — it never claims a silence it did not achieve.
/// </para>
/// <para>
/// <b>Why "unknown" is not the whole answer, and what was checked before saying so.</b> Read on a
/// live panel, the unmeasured reading is the reading of the COMMONEST account: one with no transfer
/// login at all leaves the inference nothing to infer from, so an ordinary suspension reported a
/// number as unknown for ever. Every other signal in reach was examined and none is sound.
/// <c>login_password_state</c> is an OLDER field than <c>unmanaged_logins</c>, so an agent stating it
/// may still predate the count. <c>GetAgentInfo.proto_version</c> is the only version on the wire and
/// the agent has it hard-coded at <c>1</c> for every contract revision, so it distinguishes nothing —
/// and reading it would cost this module an agent capability for a constant. There is no capability
/// handshake. Nothing else arrived in the change that added the count except a login's protocol.
/// </para>
/// <para>
/// So the inference stays and the SENTENCE changes. The unmeasured reading splits in two, which adds
/// no fourth fact: both still say the panel does not know. When logins were reported but none carried
/// a protocol, the count is unknown rather than zero — an old agent answered, and it may have missed
/// something. When NO login was reported, the useless half of "unknown" is replaced by what the
/// panel does know and what an operator can act on: the host's answer held nothing that could speak
/// to this, and the passwd file is where the answer is. Neither wording claims a measurement, and
/// neither renders an absent count as zero.
/// </para>
/// </remarks>
public static class UnmanagedLoginPolicy
{
    /// <summary>The clause the suspension attestation appends about logins the panel does not own.</summary>
    /// <param name="state">What the host answered when asked what it is doing for the account.</param>
    /// <returns>
    /// The clause to append to the attestation's "NOT covered" list — never empty, because all three
    /// readings are facts an operator has to be able to tell apart. Four wordings carry those three:
    /// the unmeasured reading is worded one way when the host reported logins that named no daemon and
    /// another when it reported no login at all, because only the second can be given an action.
    /// </returns>
    public static string Describe(AccountSuspensionStateDto state)
    {
        if (!WasMeasured(state))
        {
            if (state.FileTransferLogins.Count == 0)
            {
                return ", and no transfer login was reported, so nothing in the host's answer can say "
                    + "whether a login outside the panel's own shares this account's uid: read the "
                    + "host's passwd for entries carrying it";
            }

            return ", and the host did not say how many logins share this account's uid without the "
                + "panel having created them, so that number is unknown rather than zero";
        }

        return state.UnmanagedLogins == 0
            ? ", and no login outside the panel's own shares this account's uid"
            : $", and {state.UnmanagedLogins} login(s) sharing this account's uid that the panel did "
                + "not create and does not lock";
    }

    /// <summary>Whether the host's answer carries a measured count rather than an absent one.</summary>
    /// <param name="state">What the host answered when asked what it is doing for the account.</param>
    /// <returns><c>true</c> when the count may be stated as a fact.</returns>
    /// <remarks>
    /// The two conditions are independent and either is sufficient: a non-zero count can only come
    /// from an agent that counted, and a login carrying a stated protocol proves the answering agent
    /// speaks the contract version that introduced both fields. Neither is a guess about the agent's
    /// build; both are read out of the answer in hand.
    /// </remarks>
    private static bool WasMeasured(AccountSuspensionStateDto state)
    {
        return state.UnmanagedLogins > 0
            || state.FileTransferLogins.Any(login =>
            {
                return login.Protocol != LoginTransferProtocol.Unspecified;
            });
    }
}
