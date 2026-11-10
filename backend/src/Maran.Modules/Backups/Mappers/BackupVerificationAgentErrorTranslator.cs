using Maran.Agent.Client.Errors;
using Maran.Modules.Backups.Resources;

namespace Maran.Modules.Backups.Mappers;

/// <summary>
/// Decides whether an agent's refusal of a verification is a finding about the artifact, and names
/// the finding with this module's own code.
/// </summary>
/// <remarks>
/// <para>
/// A mapper translates; it never decides (rules/csharp.md). The decision — is this artifact damaged —
/// was made by the agent and arrives as a wire code; what is translated here is which of the agent's
/// refusals SAY so. The agent answers a corrupt, changed or inconsistent artifact with its
/// validation-failed code and an artifact it cannot find under the account with not-found. Every
/// other refusal — a busy lock, a saturated agent, a floor on free space, a destination it cannot
/// read, an unreachable agent, an input the panel itself got wrong — says nothing about the
/// artifact, and is passed back to the caller unchanged so it is neither recorded nor mailed as a
/// finding.
/// </para>
/// <para>
/// <b>The two codes are told apart by code and not by kind.</b> The agent's input-validation refusal
/// and its artifact-validation refusal are both the validation KIND, and only the second is a
/// statement about the artifact: an invalid input here would be a defect in the panel, which sends
/// the agent two identifiers it read from its own row. The agent client's code for the first is
/// <c>AgentInvalidInput</c> and for the second <c>AgentValidationFailed</c>, compared as literals for
/// the reason <c>RestoreAgentErrorTranslator</c> gives: the codes are declared in the agent client's
/// resources, which this module may not reference.
/// </para>
/// </remarks>
public static class BackupVerificationAgentErrorTranslator
{
    /// <summary>The agent client's code for an artifact the agent refused as corrupt, changed or inconsistent.</summary>
    private const string AgentValidationFailedCode = "AgentValidationFailed";

    /// <summary>
    /// The agent client's code for a sealed artifact that did not open under the destination's key, or
    /// that was sent none (issue #445).
    /// </summary>
    private const string AgentArchiveUnopenableCode = "AgentArchiveUnopenable";

    /// <summary>Names the finding an agent refusal states about the artifact, if it states one.</summary>
    /// <param name="error">The agent client's typed failure for the verification.</param>
    /// <returns>
    /// <c>BackupVerificationFailed</c> for a damaged artifact, <c>BackupVerificationArchiveKeyRejected</c>
    /// for a sealed one that does not open with the destination's key (or was sent none: the stored
    /// copy cannot be read either way), <c>BackupVerificationArtifactMissing</c>
    /// for one the agent could not find, and <c>null</c> for every refusal that is not about the
    /// artifact.
    /// </returns>
    public static string? ToFindingCode(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (string.Equals(error.Code, AgentValidationFailedCode, StringComparison.Ordinal))
        {
            return nameof(ErrorMessages.BackupVerificationFailed);
        }

        if (string.Equals(error.Code, AgentArchiveUnopenableCode, StringComparison.Ordinal))
        {
            return nameof(ErrorMessages.BackupVerificationArchiveKeyRejected);
        }

        if (string.Equals(error.Code, AgentErrorCodes.NotFound, StringComparison.Ordinal))
        {
            return nameof(ErrorMessages.BackupVerificationArtifactMissing);
        }

        return null;
    }

    /// <summary>Gives the agent's busy refusal this module's own words, and passes everything else through.</summary>
    /// <param name="error">The agent client's typed failure for a verification that could not run.</param>
    /// <returns>
    /// <c>AccountBackupOperationRunning</c> where the agent refused because the account's backup lock
    /// was held, otherwise <paramref name="error"/> unchanged.
    /// </returns>
    /// <remarks>
    /// The agent's per-account lock refuses a second operation with the wire's already-exists, whose
    /// shared sentence says nothing was created again. On a verification that sentence is false and
    /// would hide that the account was merely busy; the rename is the same one the deletion and the
    /// restore make, and it is safe here for the same reason — a verification creates nothing, so the
    /// code can only be the lock.
    /// </remarks>
    public static Error ToNotPerformed(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (string.Equals(error.Code, "AgentAlreadyExists", StringComparison.Ordinal))
        {
            return Error.Of(nameof(ErrorMessages.AccountBackupOperationRunning), ErrorType.Conflict);
        }

        return error;
    }
}
