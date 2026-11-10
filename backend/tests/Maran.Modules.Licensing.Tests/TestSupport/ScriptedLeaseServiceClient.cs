using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Models;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>
/// A licence service client that answers what the test scripts and remembers every licence id it was asked
/// about, so a test can prove both what the renewal did with an answer and that it asked at all.
/// </summary>
internal sealed class ScriptedLeaseServiceClient : ILeaseServiceClient
{
    /// <summary>The answer the next request gets.</summary>
    internal LeaseServiceAnswer Answer { get; set; } = LeaseServiceAnswer.Without(LeaseServiceAnswerKind.Unreachable);

    /// <summary>Run while a request is in flight, so a test can change the world between the question and the answer.</summary>
    internal Action? WhileAsking { get; set; }

    /// <summary>The licence id of every request, in order.</summary>
    internal List<string> Requests { get; } = [];

    /// <summary>Scripts a 200 answer carrying <paramref name="body"/>.</summary>
    /// <param name="body">The response body.</param>
    internal void Returns(string body)
    {
        Answer = new LeaseServiceAnswer(LeaseServiceAnswerKind.Received, body);
    }

    /// <inheritdoc />
    public Task<LeaseServiceAnswer> RequestAsync(string licenceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(licenceId);
        WhileAsking?.Invoke();

        return Task.FromResult(Answer);
    }
}
