namespace Maran.Modules.ServerMigration.Queries.ListRemoteServers;

/// <summary>
/// The public names the remote servers list sorts by — the <c>sort</c> parameter's vocabulary, written once and read
/// by <see cref="ListRemoteServersSorts.Map"/> and the tests.
/// </summary>
public static class RemoteServerSortFields
{
    /// <summary>The server's label.</summary>
    public const string Name = "name";

    /// <summary>The address the panel connects to.</summary>
    public const string Host = "host";

    /// <summary>How far the server has got from registered to ready.</summary>
    public const string State = "state";

    /// <summary>When the connection was last tried.</summary>
    public const string LastTestedAt = "lastTestedAt";

    /// <summary>When the server was registered.</summary>
    public const string CreatedAt = "createdAt";
}
