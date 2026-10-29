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

    /// <summary>When set, the handshake observes the caller's token the way a real call does.</summary>
    private readonly bool _honoursCancellation;

    /// <summary>When set, the handshake throws it instead of answering: a defect, not an outage.</summary>
    private readonly Exception? _defect;

    /// <summary>Creates the double over a scripted answer.</summary>
    /// <param name="answer">What the handshake returns.</param>
    /// <param name="honoursCancellation">Whether a cancelled token makes the handshake throw.</param>
    /// <param name="defect">An exception the handshake throws instead of answering, or <c>null</c>.</param>
    public StubAgentSystemClient(
        Result<AgentInfoDto> answer,
        bool honoursCancellation = false,
        Exception? defect = null)
    {
        _answer = answer;
        _honoursCancellation = honoursCancellation;
        _defect = defect;
    }

    /// <summary>An agent client with a defect of its own: its handshake throws <paramref name="defect"/>.</summary>
    /// <param name="defect">What the handshake throws; it is not any outage the real client reports as a result.</param>
    /// <returns>The double.</returns>
    public static StubAgentSystemClient Faulting(Exception defect)
    {
        return new StubAgentSystemClient(
            Result<AgentInfoDto>.Fail(Error.Of("AgentUnavailable", ErrorType.Unavailable)),
            defect: defect);
    }

    /// <summary>An agent whose handshake throws <see cref="OperationCanceledException"/> once the caller cancels.</summary>
    /// <returns>The double.</returns>
    public static StubAgentSystemClient ObservingCancellation()
    {
        return new StubAgentSystemClient(
            Result<AgentInfoDto>.Fail(Error.Of("AgentUnavailable", ErrorType.Unavailable)),
            honoursCancellation: true);
    }

    /// <summary>An agent that answers, reporting <paramref name="backupRoot"/> as its backup directory.</summary>
    /// <param name="backupRoot">The directory to report; empty models an agent older than the field.</param>
    /// <returns>The double.</returns>
    public static StubAgentSystemClient Reporting(string backupRoot)
    {
        return new StubAgentSystemClient(
            Result<AgentInfoDto>.Ok(new AgentInfoDto("1.0.0", "ubuntu", "debian", AgentProtoVersionWindow.Maximum, backupRoot)));
    }

    /// <summary>An agent that cannot be reached at all.</summary>
    /// <returns>The double.</returns>
    public static StubAgentSystemClient Unreachable()
    {
        return new StubAgentSystemClient(
            Result<AgentInfoDto>.Fail(Error.Of("AgentUnavailable", ErrorType.Unavailable)));
    }

    /// <summary>An agent that answered in a contract revision the panel does not drive.</summary>
    /// <returns>The double.</returns>
    public static StubAgentSystemClient Incompatible()
    {
        return new StubAgentSystemClient(
            Result<AgentInfoDto>.Fail(Error.Of(AgentProtoVersionWindow.IncompatibleCode, ErrorType.Unavailable)));
    }

    /// <inheritdoc/>
    public Task<Result<AgentInfoDto>> GetInfoAsync(CancellationToken ct)
    {
        if (_defect is not null)
        {
            throw _defect;
        }

        if (_honoursCancellation)
        {
            ct.ThrowIfCancellationRequested();
        }

        return Task.FromResult(_answer);
    }
}
