using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SystemService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Backups.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentSystemClient"/> double answering a scripted handshake — or refusing to.
/// </summary>
/// <remarks>
/// The two answers it can give are the two states the destinations listing has to tell apart: an
/// agent that stated the directory it backs up into, and an agent that stated nothing, whether
/// because it could not be reached or because it predates the field. A double that could only
/// succeed would leave the second state — the one where the panel must admit it does not know the
/// path — untested.
/// </remarks>
public sealed class StubAgentSystemClient : IAgentSystemClient
{
    /// <summary>The scripted answer to every handshake.</summary>
    private readonly Result<AgentInfoDto> _answer;

    /// <summary>Creates the double over a scripted answer.</summary>
    /// <param name="answer">What the handshake returns.</param>
    public StubAgentSystemClient(Result<AgentInfoDto> answer)
    {
        _answer = answer;
    }

    /// <summary>An agent that answers, reporting <paramref name="backupRoot"/> as its backup directory.</summary>
    /// <param name="backupRoot">The directory to report; empty models an agent older than the field.</param>
    /// <returns>The double.</returns>
    public static StubAgentSystemClient Reporting(string backupRoot)
    {
        return new StubAgentSystemClient(
            Result<AgentInfoDto>.Ok(new AgentInfoDto("1.0.0", "ubuntu", "debian", 1, backupRoot)));
    }

    /// <summary>An agent that cannot be reached at all.</summary>
    /// <returns>The double.</returns>
    public static StubAgentSystemClient Unreachable()
    {
        return new StubAgentSystemClient(
            Result<AgentInfoDto>.Fail(Error.Of("AgentUnavailable", ErrorType.Unavailable)));
    }

    /// <inheritdoc/>
    public Task<Result<AgentInfoDto>> GetInfoAsync(CancellationToken ct)
    {
        return Task.FromResult(_answer);
    }
}
