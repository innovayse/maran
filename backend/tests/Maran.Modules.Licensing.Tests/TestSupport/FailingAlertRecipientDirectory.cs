using Maran.Sdk.Interfaces;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>
/// An <see cref="IAlertRecipientDirectory"/> double whose first reads fail, so an alert that could not find where to
/// go is observable, and then answers a fixed address.
/// </summary>
public sealed class FailingAlertRecipientDirectory : IAlertRecipientDirectory
{
    /// <summary>The address answered once the failures are spent.</summary>
    private readonly string _recipient;

    /// <summary>How many reads still fail.</summary>
    private int _failuresLeft;

    /// <summary>Creates the double.</summary>
    /// <param name="failures">How many reads fail before the first one succeeds.</param>
    /// <param name="recipient">The address answered afterwards.</param>
    public FailingAlertRecipientDirectory(int failures, string recipient)
    {
        _failuresLeft = failures;
        _recipient = recipient;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetAlertRecipientsAsync(CancellationToken cancellationToken)
    {
        if (_failuresLeft > 0)
        {
            _failuresLeft--;
            throw new InvalidOperationException("planted recipient read failure");
        }

        return Task.FromResult<IReadOnlyList<string>>([_recipient]);
    }
}
