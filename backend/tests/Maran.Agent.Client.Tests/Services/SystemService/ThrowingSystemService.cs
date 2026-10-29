using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.SystemService;

/// <summary>
/// Stub of <see cref="ISystemServiceInvoker"/> that fails with a given exception, optionally cancelling
/// a caller's token first, so the order "the caller gave up, then the transport reported it" is
/// reproducible without a socket.
/// </summary>
internal sealed class ThrowingSystemService : ISystemServiceInvoker
{
    /// <summary>The exception every call throws.</summary>
    private readonly Exception _failure;

    /// <summary>A source cancelled just before the exception is thrown, or null to leave the caller's token alone.</summary>
    private readonly CancellationTokenSource? _cancelFirst;

    /// <summary>Creates a stub that throws <paramref name="failure"/> on every call.</summary>
    /// <param name="failure">The exception to throw.</param>
    /// <param name="cancelFirst">A source to cancel before throwing, or null.</param>
    public ThrowingSystemService(Exception failure, CancellationTokenSource? cancelFirst = null)
    {
        _failure = failure;
        _cancelFirst = cancelFirst;
    }

    /// <inheritdoc/>
    public async Task<GetAgentInfoResponse> GetAgentInfoAsync(CancellationToken ct)
    {
        if (_cancelFirst is not null)
        {
            await _cancelFirst.CancelAsync();
        }

        throw _failure;
    }
}
