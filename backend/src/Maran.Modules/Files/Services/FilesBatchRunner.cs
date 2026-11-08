using Maran.Modules.Files.Common;
using Maran.Modules.Files.Models;
using Maran.Modules.Files.Options;
using Maran.Modules.Files.Resources;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Files.Services;

/// <summary>
/// Runs one operation over a batch of paths: caps it before the first agent call, runs the items in
/// order, audits each on its own and reports each outcome.
/// </summary>
/// <remarks>
/// <para>
/// A failure of one item does not stop the others; it is reported on that item with its code and its
/// sentence in the caller's language. The batch as a whole fails only when it cannot start at all: empty
/// (<c>FilesBatchEmpty</c>) or over <see cref="FilesOptions.MaxBatchItems"/> (<c>FilesBatchTooLarge</c>) —
/// and both are decided before the agent is called once.
/// </para>
/// <para>
/// Every item writes its own journal entry, success or failure, so a partly completed batch is exactly
/// as visible to an operator as a whole one. The journal writes do not take the request's token: an item
/// that was done stays recorded when the caller goes away. Cancellation of the request itself is not an
/// item failure and propagates.
/// </para>
/// </remarks>
public sealed class FilesBatchRunner
{
    /// <summary>This module's audit journal.</summary>
    private readonly FilesAuditJournal _journal;

    /// <summary>Resolves a failure's code to the sentence in the caller's culture.</summary>
    private readonly IErrorTextProvider _texts;

    /// <summary>The batch ceiling, read at each run so a reloaded setting takes effect.</summary>
    private readonly IOptionsMonitor<FilesOptions> _options;

    /// <summary>Creates the runner.</summary>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="texts">Resolves an error code to its sentence.</param>
    /// <param name="options">The module's limits.</param>
    public FilesBatchRunner(FilesAuditJournal journal, IErrorTextProvider texts, IOptionsMonitor<FilesOptions> options)
    {
        _journal = journal;
        _texts = texts;
        _options = options;
    }

    /// <summary>Runs <paramref name="operate"/> over every subject.</summary>
    /// <param name="account">The account the batch is on, already admitted by <see cref="FilesAccountGate"/>.</param>
    /// <param name="action">The audit action each item is recorded under.</param>
    /// <param name="subjects">The paths (or trash ids) of the batch, already validated.</param>
    /// <param name="origin">The caller's address and user agent.</param>
    /// <param name="operate">Does the operation for one subject; its failure is that item's outcome.</param>
    /// <param name="cancellationToken">Cancels the batch; the items already done stay recorded.</param>
    /// <returns>One outcome per subject in order — or <c>FilesBatchEmpty</c> or <c>FilesBatchTooLarge</c>.</returns>
    public async Task<Result<FileBatchResultDto>> RunAsync(
        AccountSnapshot account,
        string action,
        IReadOnlyList<string> subjects,
        FilesCallOrigin origin,
        Func<string, CancellationToken, Task<Result>> operate,
        CancellationToken cancellationToken)
    {
        if (subjects.Count == 0)
        {
            return await RefuseAsync(account, action, origin, nameof(ErrorMessages.FilesBatchEmpty));
        }

        if (subjects.Count > _options.CurrentValue.MaxBatchItems)
        {
            return await RefuseAsync(account, action, origin, nameof(ErrorMessages.FilesBatchTooLarge));
        }

        var outcomes = new List<FileBatchItemResultDto>(subjects.Count);
        foreach (var subject in subjects)
        {
            var done = await operate(subject, cancellationToken);
            var journalSubject = FilesAuditJournal.SubjectOf(account.Username, subject);

            if (done.IsSuccess)
            {
                await _journal.RecordSuccessAsync(action, account.Id, journalSubject, origin, CancellationToken.None);
                outcomes.Add(new FileBatchItemResultDto(subject, true, null, null));
            }
            else
            {
                await _journal.RecordFailureAsync(action, account.Id, journalSubject, origin, CancellationToken.None);
                outcomes.Add(
                    new FileBatchItemResultDto(subject, false, done.Error!.Code, _texts.Resolve(done.Error.Code)));
            }
        }

        return Result<FileBatchResultDto>.Ok(new FileBatchResultDto(outcomes));
    }

    /// <summary>Journals a batch that could not start and returns it as the typed failure.</summary>
    /// <param name="account">The account the batch was on.</param>
    /// <param name="action">The audit action attempted.</param>
    /// <param name="origin">The caller's address and user agent.</param>
    /// <param name="code">The error code to answer with; always a validation failure.</param>
    /// <returns>The failed result.</returns>
    private async Task<Result<FileBatchResultDto>> RefuseAsync(
        AccountSnapshot account,
        string action,
        FilesCallOrigin origin,
        string code)
    {
        await _journal.RecordFailureAsync(
            action,
            account.Id,
            FilesAuditJournal.SubjectOf(account.Username, string.Empty),
            origin,
            CancellationToken.None);
        return Result<FileBatchResultDto>.Fail(Error.Of(code, ErrorType.Validation));
    }
}
