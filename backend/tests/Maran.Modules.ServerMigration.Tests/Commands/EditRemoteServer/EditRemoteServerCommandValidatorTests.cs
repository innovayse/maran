using Maran.Modules.ServerMigration.Commands.EditRemoteServer;

namespace Maran.Modules.ServerMigration.Tests.Commands.EditRemoteServer;

/// <summary>The edit is held to the bounds the registration is: one set of rules, so the two cannot accept different servers.</summary>
public sealed class EditRemoteServerCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly EditRemoteServerCommandValidator Validator = new();

    /// <summary>Well-formed values are accepted, and a port out of range is refused with the registration's code.</summary>
    [Fact]
    public void The_edit_applies_the_registrations_rules()
    {
        var accepted = Validator.Validate(new EditRemoteServerCommand("name", "old.example.net", 22, "maranmig"));
        var refused = Validator.Validate(new EditRemoteServerCommand("name", "old.example.net", 0, "maranmig"));

        Assert.True(accepted.IsValid);
        Assert.Contains(refused.Errors, error =>
        {
            return error.ErrorCode == "RemoteServerPortInvalid";
        });
    }
}
