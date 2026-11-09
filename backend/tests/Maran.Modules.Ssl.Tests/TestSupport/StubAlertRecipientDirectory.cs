using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="IAlertRecipientDirectory"/> double standing in for the Notifications module's
/// answer to "where do operator alerts go".
/// </summary>
/// <remarks>
/// A double because the real one lives in another module, which this test project may not reference.
/// </remarks>
public sealed class StubAlertRecipientDirectory : IAlertRecipientDirectory
{
    /// <summary>The address to answer with, or null for a panel that has none configured.</summary>
    private readonly string? _recipient;

    /// <summary>Creates the double.</summary>
    /// <param name="recipient">The address to answer with; null for a panel with no alert address.</param>
    public StubAlertRecipientDirectory(string? recipient)
    {
        _recipient = recipient;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetAlertRecipientsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> recipients = _recipient is null ? [] : [_recipient];
        return Task.FromResult(recipients);
    }
}
