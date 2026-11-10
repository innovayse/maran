using Maran.Modules.ServerMigration.Validators;

namespace Maran.Modules.ServerMigration.Commands.EditRemoteServer;

/// <summary>
/// Holds <see cref="EditRemoteServerCommand"/> to the bounds every remote server is held to; the rules are
/// <see cref="RemoteServerFieldsValidator{TCommand}"/>'s, shared with the registration.
/// </summary>
public sealed class EditRemoteServerCommandValidator : RemoteServerFieldsValidator<EditRemoteServerCommand>
{
}
