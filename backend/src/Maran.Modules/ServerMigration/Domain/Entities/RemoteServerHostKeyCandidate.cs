namespace Maran.Modules.ServerMigration.Domain.Entities;

/// <summary>
/// One host key a remote server offered at the last scan, waiting for the administrator to compare its
/// fingerprint with the one the server's own console shows. It exists only inside its <see cref="RemoteServer"/>,
/// which creates and replaces it.
/// </summary>
public sealed class RemoteServerHostKeyCandidate
{
    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The server that offered the key.</summary>
    public Guid RemoteServerId { get; private set; }

    /// <summary>The key's algorithm, for example <c>ssh-ed25519</c>.</summary>
    public string Algorithm { get; private set; }

    /// <summary>The key itself (base64), exactly as the server offered it.</summary>
    public string Key { get; private set; }

    /// <summary>The key's fingerprint, <c>SHA256:</c> and 43 base64 characters; what the administrator compares and types.</summary>
    public string Fingerprint { get; private set; }

    /// <summary>Creates the row. Only <see cref="RemoteServer"/> calls it.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="remoteServerId">The server that offered the key.</param>
    /// <param name="algorithm">The key's algorithm.</param>
    /// <param name="key">The key (base64).</param>
    /// <param name="fingerprint">The key's fingerprint.</param>
    internal RemoteServerHostKeyCandidate(Guid id, Guid remoteServerId, string algorithm, string key, string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(algorithm.Length, RemoteServer.HostKeyAlgorithmMaxLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(key.Length, RemoteServer.HostKeyMaxLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fingerprint.Length, RemoteServer.FingerprintLength);

        Id = id;
        RemoteServerId = remoteServerId;
        Algorithm = algorithm;
        Key = key;
        Fingerprint = fingerprint;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private RemoteServerHostKeyCandidate()
    {
        Algorithm = string.Empty;
        Key = string.Empty;
        Fingerprint = string.Empty;
    }
}
