using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SystemService;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.HealthChecks;

/// <summary>
/// An agent system client that answers however a test needs it to, including not at all.
/// </summary>
/// <remarks>
/// The "not at all" case is the one that matters: a hung agent is the failure a health probe most
/// has to survive, and it cannot be produced with a stub that always returns.
/// </remarks>
public sealed class StubAgentSystemClient : IAgentSystemClient
{
    private readonly Result<AgentInfoDto>? _answer;
    private readonly Exception? _failure;

    /// <summary>Answers with a fixed result.</summary>
    /// <param name="answer">What <see cref="GetInfoAsync"/> returns.</param>
    public StubAgentSystemClient(Result<AgentInfoDto> answer)
    {
        _answer = answer;
    }

    /// <summary>Throws instead of answering.</summary>
    /// <param name="failure">The exception to throw.</param>
    public StubAgentSystemClient(Exception failure)
    {
        _failure = failure;
    }

    /// <summary>Never answers on its own; only cancellation ends the call.</summary>
    private StubAgentSystemClient()
    {
    }

    /// <summary>A client that hangs until its caller gives up.</summary>
    /// <returns>The stub.</returns>
    public static StubAgentSystemClient ThatNeverAnswers()
    {
        return new StubAgentSystemClient();
    }

    /// <inheritdoc/>
    public async Task<Result<AgentInfoDto>> GetInfoAsync(CancellationToken ct)
    {
        if (_failure is not null)
        {
            throw _failure;
        }

        if (_answer is { } answer)
        {
            return await Task.FromResult(answer);
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        throw new InvalidOperationException("unreachable: the delay above only ends by cancellation");
    }
}
