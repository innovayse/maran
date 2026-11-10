using Maran.Modules.ServerMigration.Validators;

namespace Maran.Modules.ServerMigration.Commands.AddRemoteServer;

/// <summary>
/// Holds <see cref="AddRemoteServerCommand"/> to the bounds every remote server is held to; the rules are
/// <see cref="RemoteServerFieldsValidator{TCommand}"/>'s, shared with the edit.
/// </summary>
public sealed class AddRemoteServerCommandValidator : RemoteServerFieldsValidator<AddRemoteServerCommand>
{
}
