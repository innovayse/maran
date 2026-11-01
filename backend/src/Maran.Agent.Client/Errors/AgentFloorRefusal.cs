using Maran.Agent.Client.Resources;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Errors;

/// <summary>
/// Recognises the agent's refusal to start a write below its free-space floor (issue #148), so a
/// caller that otherwise folds an agent failure into a code of its own can pass this one through.
/// </summary>
/// <remarks>
/// The floor's refusal is the one agent failure a caller must never re-word: it says nothing broke
/// and the server is nearly out of disk space, which is the sentence the operator needs. The panel's
/// own certificate issuance is the worked example (issue #132): every other challenge-write failure
/// becomes <c>AcmeChallengeWriteFailed</c>, and this one keeps its own code.
/// </remarks>
public static class AgentFloorRefusal
{
    /// <summary>The machine code the agent's floor refusal carries once translated.</summary>
    public const string Code = nameof(ErrorMessages.AgentBelowFreeSpaceFloor);

    /// <summary>Whether <paramref name="error"/> is the agent's free-space floor refusal.</summary>
    /// <param name="error">A failure an agent client returned.</param>
    /// <returns>True for the floor's refusal, false for every other failure.</returns>
    public static bool Is(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return string.Equals(error.Code, Code, StringComparison.Ordinal);
    }
}
