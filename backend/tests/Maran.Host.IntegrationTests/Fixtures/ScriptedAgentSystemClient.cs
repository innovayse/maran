using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SystemService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An agent handshake whose answer the test chooses, and can change between two requests. It sits at the
/// <see cref="IAgentSystemClient"/> seam, which is where the contract window is judged: the readiness
/// tests need an agent that reports a revision outside the window, and no real agent can be made to.
/// </summary>
public sealed class ScriptedAgentSystemClient : IAgentSystemClient
{
    /// <summary>The answer every call returns; replaced by a test to model an upgrade finishing.</summary>
    private Result<AgentInfoDto> _answer;

    /// <summary>Creates the client with its first answer.</summary>
    /// <param name="answer">What the first call returns.</param>
    public ScriptedAgentSystemClient(Result<AgentInfoDto> answer)
    {
        _answer = answer;
    }

    /// <summary>A handshake the panel admits: an agent speaking the newest revision it drives.</summary>
    /// <returns>The successful answer.</returns>
    public static Result<AgentInfoDto> Compatible()
    {
        return Result<AgentInfoDto>.Ok(
            new AgentInfoDto("test", "debian", "debian", AgentProtoVersionWindow.Maximum, "/var/backups/maran"));
    }

    /// <summary>The refusal the real system client returns for a revision outside the window.</summary>
    /// <returns>The failed answer.</returns>
    public static Result<AgentInfoDto> Incompatible()
    {
        return Result<AgentInfoDto>.Fail(Error.Of(AgentProtoVersionWindow.IncompatibleCode, ErrorType.Unavailable));
    }

    /// <summary>Changes what the following calls return.</summary>
    /// <param name="answer">The new answer.</param>
    public void Answer(Result<AgentInfoDto> answer)
    {
        _answer = answer;
    }

    /// <inheritdoc />
    public Task<Result<AgentInfoDto>> GetInfoAsync(CancellationToken ct)
    {
        return Task.FromResult(_answer);
    }
}
