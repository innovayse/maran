using Maran.Modules.Files.Commands.EmptyTrash;

namespace Maran.Modules.Files.Tests.Commands.EmptyTrash;

/// <summary>Holds <see cref="EmptyTrashCommandValidator"/> to naming an account, the one thing the command carries.</summary>
public sealed class EmptyTrashCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly EmptyTrashCommandValidator Validator = new();

    /// <summary>A command that names an account is accepted.</summary>
    [Fact]
    public void A_command_that_names_an_account_is_valid()
    {
        Assert.True(Validator.Validate(new EmptyTrashCommand(Guid.NewGuid())).IsValid);
    }

    /// <summary>A command with no account is refused.</summary>
    [Fact]
    public void A_command_with_no_account_is_refused()
    {
        Assert.False(Validator.Validate(new EmptyTrashCommand(Guid.Empty)).IsValid);
    }
}
