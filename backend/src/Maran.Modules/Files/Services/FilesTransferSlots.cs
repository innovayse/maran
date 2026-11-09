using Maran.Modules.Files.Models;
using Maran.Modules.Files.Options;
using Maran.Modules.Files.Resources;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Files.Services;

/// <summary>
/// Counts the uploads and downloads in flight in this panel process, per account and in total, and
/// refuses a new one past the bounds.
/// </summary>
/// <remarks>
/// <para>
/// A transfer holds an agent stream and an API thread for as long as it lasts, which a request rate
/// cannot bound. Without this a customer opening transfers in a loop holds every stream. A slot is
/// taken BEFORE the agent is called and held for the life of the stream; the lease gives it back on
/// disposal, including when the client aborts. A refused transfer is refused at once, never waited for.
/// </para>
/// <para>
/// A singleton, because the count has to be shared by every request; nothing here is persisted — a
/// restart also ends every stream, so starting from zero is the true count. The account bound is
/// checked first so a customer over their own limit never learns how busy the server is: an account at
/// its bound is told one of its own transfers is still going (<see cref="ErrorType.Conflict"/>: wait),
/// an account refused by the panel-wide bound did nothing wrong (<see cref="ErrorType.Unavailable"/>:
/// try again).
/// </para>
/// </remarks>
public sealed class FilesTransferSlots
{
    /// <summary>The bounds, read at each acquisition so a reloaded setting takes effect.</summary>
    private readonly IOptionsMonitor<FilesOptions> _options;

    /// <summary>Guards both counters, which must change together.</summary>
    private readonly object _gate = new();

    /// <summary>The transfers in flight per account; an account with none has no entry.</summary>
    private readonly Dictionary<Guid, int> _perAccount = [];

    /// <summary>The transfers in flight across every account.</summary>
    private int _total;

    /// <summary>Creates the counter.</summary>
    /// <param name="options">The per-account and panel-wide bounds.</param>
    public FilesTransferSlots(IOptionsMonitor<FilesOptions> options)
    {
        _options = options;
    }

    /// <summary>Takes a slot for a transfer of <paramref name="accountId"/>, or refuses.</summary>
    /// <param name="accountId">The account whose file is about to be transferred.</param>
    /// <returns>A lease to dispose when the transfer is over — or <c>FilesAccountBusy</c>, or <c>FilesServerBusy</c>.</returns>
    public Result<FilesTransferLease> TryAcquire(Guid accountId)
    {
        var bounds = _options.CurrentValue;

        lock (_gate)
        {
            _perAccount.TryGetValue(accountId, out var held);
            if (held >= bounds.MaxConcurrentTransfersPerAccount)
            {
                return Result<FilesTransferLease>.Fail(
                    Error.Of(nameof(ErrorMessages.FilesAccountBusy), ErrorType.Conflict));
            }

            if (_total >= bounds.MaxConcurrentTransfersGlobal)
            {
                return Result<FilesTransferLease>.Fail(
                    Error.Of(nameof(ErrorMessages.FilesServerBusy), ErrorType.Unavailable));
            }

            _perAccount[accountId] = held + 1;
            _total++;
        }

        return Result<FilesTransferLease>.Ok(new FilesTransferLease(() =>
        {
            Release(accountId);
        }));
    }

    /// <summary>Gives back one slot taken for <paramref name="accountId"/>.</summary>
    /// <param name="accountId">The account the slot was taken for.</param>
    private void Release(Guid accountId)
    {
        lock (_gate)
        {
            _total--;
            if (_perAccount[accountId] <= 1)
            {
                _perAccount.Remove(accountId);
            }
            else
            {
                _perAccount[accountId]--;
            }
        }
    }
}
