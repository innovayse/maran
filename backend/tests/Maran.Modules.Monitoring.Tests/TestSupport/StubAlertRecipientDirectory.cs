using Maran.Sdk.Interfaces;

namespace Maran.Modules.Monitoring.Tests.TestSupport;

/// <summary>
/// An <see cref="IAlertRecipientDirectory"/> double standing in for the Notifications module's
/// answer to "where do operator alerts go".
/// </summary>
/// <remarks>
/// A double and not the real thing precisely because the real one lives in another module, which
/// this test project may not reference. That the seam can be doubled this cheaply is the point of
/// the split: Monitoring's alert behaviour is now testable without a mail server, a settings row, or
/// the Notifications assembly.
/// </remarks>
public sealed class StubAlertRecipientDirectory : IAlertRecipientDirectory
{
    /// <summary>The address to answer with, or null for a panel that has none configured.</summary>
    private readonly string? _recipient;

    /// <summary>Creates the double.</summary>
    /// <param name="recipient">The address to answer with; null for a panel with no alert address.</param>
    public StubAlertRecipientDirectory(string? recipient = null)
    {
        _recipient = recipient;
    }

    /// <inheritdoc />
    public Task<string?> GetAlertRecipientAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_recipient);
    }
}
