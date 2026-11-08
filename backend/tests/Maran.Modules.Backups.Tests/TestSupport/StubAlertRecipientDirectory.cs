using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Tests.TestSupport;

/// <summary>
/// An <see cref="IAlertRecipientDirectory"/> double standing in for the Notifications module's
/// answer to "where do operator alerts go".
/// </summary>
/// <remarks>
/// A double because the real one lives in another module, which this test project may not reference.
/// </remarks>
public sealed class StubAlertRecipientDirectory : IAlertRecipientDirectory
{
    /// <summary>The addresses to answer with; empty for a panel that has none configured.</summary>
    private readonly IReadOnlyList<string> _recipients;

    /// <summary>Creates the double.</summary>
    /// <param name="recipient">The address to answer with; null for a panel with no alert address.</param>
    public StubAlertRecipientDirectory(string? recipient)
    {
        _recipients = recipient is null ? [] : [recipient];
    }

    /// <summary>Creates the double with a list of addresses.</summary>
    /// <param name="recipients">The addresses to answer with.</param>
    private StubAlertRecipientDirectory(IReadOnlyList<string> recipients)
    {
        _recipients = recipients;
    }

    /// <summary>Creates a double answering several addresses, or none.</summary>
    /// <param name="recipients">The addresses to answer with.</param>
    /// <returns>The double.</returns>
    public static StubAlertRecipientDirectory Of(IReadOnlyList<string> recipients)
    {
        return new StubAlertRecipientDirectory(recipients);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetAlertRecipientsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_recipients);
    }
}
