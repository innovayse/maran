using Maran.Sdk.Interfaces;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Wraps a journal and reports, once, that its first entry is stored. It lets the code that raised an alert
/// remember the alert as told at the moment the journal row exists, however the rest of the alert then fares.
/// </summary>
/// <remarks>
/// A row in the journal is the fact an operator investigates from; a mail, a webhook or a feed entry that did not
/// follow is somebody else's system (see <see cref="Maran.Sdk.Alerts.OperatorAlertDispatcher"/>). Remembering the alert as told only
/// when the whole call returned would make a failure AFTER the row repeat the row on every retry.
/// </remarks>
internal sealed class JournalledSignalJournal : ISystemAuditJournal
{
    /// <summary>The journal that stores the entries.</summary>
    private readonly ISystemAuditJournal _inner;

    /// <summary>Called once, after the first entry was stored.</summary>
    private readonly Func<CancellationToken, Task> _afterFirstEntry;

    /// <summary>Whether <see cref="_afterFirstEntry"/> has run.</summary>
    private bool _signalled;

    /// <summary>Creates the wrapper.</summary>
    /// <param name="inner">The journal that stores the entries.</param>
    /// <param name="afterFirstEntry">Called once, after the first entry was stored.</param>
    public JournalledSignalJournal(ISystemAuditJournal inner, Func<CancellationToken, Task> afterFirstEntry)
    {
        _inner = inner;
        _afterFirstEntry = afterFirstEntry;
    }

    /// <inheritdoc />
    public async Task RecordSystemAsync(string action, string subject, bool succeeded, CancellationToken cancellationToken)
    {
        await _inner.RecordSystemAsync(action, subject, succeeded, cancellationToken);

        if (_signalled)
        {
            return;
        }

        _signalled = true;
        await _afterFirstEntry(cancellationToken);
    }
}
