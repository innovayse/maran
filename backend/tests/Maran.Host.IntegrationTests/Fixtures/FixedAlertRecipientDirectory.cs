using Maran.Sdk.Interfaces;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Names one address as the place operator alerts go, standing in for the stored outgoing-mail
/// settings so a test can see an alert become a mail request without configuring an SMTP server.
/// </summary>
/// <remarks>
/// The inverse is the real directory, which answers nothing on a host with no mail settings: the
/// tests that prove the skipped path leave this out on purpose.
/// </remarks>
public sealed class FixedAlertRecipientDirectory : IAlertRecipientDirectory
{
    /// <summary>The address every alert is sent to.</summary>
    public const string Address = "operator@example.com";

    /// <summary>Answers the fixed address.</summary>
    /// <param name="cancellationToken">Cancellation for the read.</param>
    /// <returns>The address operator alerts are sent to.</returns>
    public Task<IReadOnlyList<string>> GetAlertRecipientsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<string>>([Address]);
    }
}
