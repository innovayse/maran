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
/// the contract as of the change that introduced the policy (issue #88). A 0 is what protobuf reads
/// from a sender that does not set the field at all, and it is outside the window too.
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
    public const uint Minimum = 2;

    /// <summary>The newest agent contract revision this panel drives: the one it was built against.</summary>
    public const uint Maximum = 2;

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
