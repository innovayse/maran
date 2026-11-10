namespace Maran.Modules.ServerMigration.Interfaces;

/// <summary>
/// The fields an administrator states to register a remote server or to change where one points. The add and
/// the edit command both carry them, and one validator holds both to the same bounds
/// (<see cref="Maran.Modules.ServerMigration.Validators.RemoteServerFieldsValidator{TCommand}"/>).
/// </summary>
public interface IRemoteServerFields
{
    /// <summary>The label the administrator knows the server by.</summary>
    string Name { get; }

    /// <summary>The server's DNS name or IP address literal.</summary>
    string Host { get; }

    /// <summary>The TCP port of the server's SSH daemon.</summary>
    int Port { get; }

    /// <summary>The login the panel's key is installed for on the server.</summary>
    string SshUser { get; }

    /// <summary>
    /// The addresses the server may accept the panel's key from, as a comma-separated list of addresses or ranges,
    /// or <c>null</c> when the key may be used from anywhere.
    /// </summary>
    string? AllowedFrom { get; }
}
