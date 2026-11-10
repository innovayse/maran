using Google.Protobuf;
using Maran.Agent.V1;
using Maran.SharedKernel.Security;

namespace Maran.Agent.Client.Services.TransferService;

/// <summary>Restates the panel's source endpoint and credential on the wire, and names the secret the credential holds.</summary>
internal static class TransferRequestMapper
{
    /// <summary>Projects a panel endpoint onto the wire message.</summary>
    /// <param name="endpoint">The source the caller named.</param>
    /// <returns>The wire message, which never leaves this project.</returns>
    /// <remarks>
    /// The port is passed through unchanged. A value outside 1..65535 is the agent's to refuse, and the
    /// conversion cannot turn a refused value into an accepted one: a negative port becomes a number far above
    /// 65535, and none of the other values is altered.
    /// </remarks>
    public static SourceEndpoint ToWire(AgentSourceEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return new SourceEndpoint
        {
            Host = endpoint.Host,
            Port = unchecked((uint)endpoint.Port),
            User = endpoint.User,
        };
    }

    /// <summary>Projects a panel credential onto the wire message, revealing the private key.</summary>
    /// <param name="credential">The credential the caller holds.</param>
    /// <returns>The wire message, which never leaves this project.</returns>
    /// <remarks>
    /// The private key is revealed here and nowhere else in this client, which is what makes "where does the
    /// secret escape" a one-search question.
    /// </remarks>
    public static SourceCredential ToWire(AgentSourceCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        return new SourceCredential
        {
            PrivateKey = ByteString.CopyFromUtf8(credential.PrivateKey.Reveal()),
            HostKey = credential.HostKey,
        };
    }

    /// <summary>Writes a pull request onto the wire.</summary>
    /// <param name="request">The pull.</param>
    /// <returns>The wire request.</returns>
    public static PullAccountFilesRequest ToWire(AgentFilePullRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new PullAccountFilesRequest
        {
            Endpoint = ToWire(request.Endpoint),
            Credential = ToWire(request.Credential),
            AccountUsername = request.AccountUsername,
            SourceHome = request.SourceHome,
            Phase = request.Phase == AgentPullPhase.Final ? PullPhase.Final : PullPhase.Initial,
            BandwidthLimitKib = unchecked((uint)Math.Max(0, request.BandwidthLimitKib)),
            ExpectedBytes = unchecked((ulong)Math.Max(0L, request.ExpectedBytes)),
        };
    }

    /// <summary>Writes a database transfer request onto the wire.</summary>
    /// <param name="request">The transfer.</param>
    /// <returns>The wire request.</returns>
    public static StreamRemoteDatabaseRequest ToWire(AgentDatabaseTransferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new StreamRemoteDatabaseRequest
        {
            Endpoint = ToWire(request.Endpoint),
            Credential = ToWire(request.Credential),
            SourceDatabase = request.SourceDatabase,
            AccountUsername = request.AccountUsername,
            TargetDatabase = request.TargetDatabase,
            MaxBytes = unchecked((ulong)Math.Max(0L, request.MaxBytes)),
        };
    }

    /// <summary>Names the secret a credential holds, so a translator can redact it from a quote.</summary>
    /// <param name="credential">The credential the caller holds.</param>
    /// <returns>The private key.</returns>
    public static SensitiveString SecretOf(AgentSourceCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        return credential.PrivateKey;
    }
}
