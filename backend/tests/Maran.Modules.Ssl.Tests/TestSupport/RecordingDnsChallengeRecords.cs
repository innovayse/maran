using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="IDnsChallengeRecords"/> double: answers with a prepared availability or a prepared refusal and
/// records every record it was asked to publish and to remove, in order.
/// </summary>
/// <remarks>
/// The DNS module is not referenced from this project, so this is the whole of the other side of the door. What
/// it records is what the certificate module did to the zone: which account, which name, which value, and
/// whether each record was taken out again.
/// </remarks>
public sealed class RecordingDnsChallengeRecords : IDnsChallengeRecords
{
    /// <summary>One record that was published.</summary>
    /// <param name="AccountId">The account whose zone received it.</param>
    /// <param name="Hostname">The name being validated.</param>
    /// <param name="Value">The TXT value.</param>
    /// <param name="Handle">The receipt handed back.</param>
    public sealed record PublishedRecord(Guid AccountId, string Hostname, string Value, DnsChallengeHandle Handle);

    /// <summary>What <see cref="CheckAsync"/> answers.</summary>
    public DnsChallengeAvailability Availability { get; set; } = DnsChallengeAvailability.Available;

    /// <summary>When set, every publish fails with it.</summary>
    public Error? PublishFailure { get; set; }

    /// <summary>When set, every removal fails with it.</summary>
    public Error? RemoveFailure { get; set; }

    /// <summary>Whether every removal throws, as a database failure would.</summary>
    public bool RemoveThrows { get; set; }

    /// <summary>Runs after each successful publish, with how many have been published so far.</summary>
    public Action<int>? AfterPublish { get; set; }

    /// <summary>Every (account, name) the availability was asked about, in order.</summary>
    public List<(Guid AccountId, string Hostname)> Checked { get; } = [];

    /// <summary>Every record published, in order.</summary>
    public List<PublishedRecord> Published { get; } = [];

    /// <summary>The receipt of every record asked to be removed, in order, whether or not the removal succeeded.</summary>
    public List<DnsChallengeHandle> RemovalsAsked { get; } = [];

    /// <summary>The cancellation token each removal was given, so a test can state that removal ignores the caller's.</summary>
    public List<CancellationToken> RemovalTokens { get; } = [];

    /// <inheritdoc />
    public Task<DnsChallengeAvailability> CheckAsync(Guid accountId, string hostname, CancellationToken cancellationToken)
    {
        Checked.Add((accountId, hostname));

        return Task.FromResult(Availability);
    }

    /// <inheritdoc />
    public Task<Result<DnsChallengeHandle>> PublishAsync(
        Guid accountId,
        string hostname,
        string txtValue,
        CancellationToken cancellationToken)
    {
        if (PublishFailure is not null)
        {
            return Task.FromResult(Result<DnsChallengeHandle>.Fail(PublishFailure));
        }

        var handle = new DnsChallengeHandle(accountId, Guid.NewGuid(), Guid.NewGuid());
        Published.Add(new PublishedRecord(accountId, hostname, txtValue, handle));
        AfterPublish?.Invoke(Published.Count);

        return Task.FromResult(Result<DnsChallengeHandle>.Ok(handle));
    }

    /// <inheritdoc />
    public Task<Result> RemoveAsync(DnsChallengeHandle handle, CancellationToken cancellationToken)
    {
        RemovalsAsked.Add(handle);
        RemovalTokens.Add(cancellationToken);
        if (RemoveThrows)
        {
            throw new InvalidOperationException("the database is unreachable");
        }

        return Task.FromResult(RemoveFailure is null ? Result.Ok() : Result.Fail(RemoveFailure));
    }
}
