using FluentValidation.Results;
using Maran.Modules.Files.Commands.RestoreTrashEntries;

namespace Maran.Modules.Files.Tests.Commands.RestoreTrashEntries;

/// <summary>
/// Holds <see cref="RestoreTrashEntriesCommandValidator"/> to its contract for restoring entries: every path goes through the one
/// path rule, the trash directory is reserved, and each refusal carries the code the customer is shown.
/// </summary>
public sealed class RestoreTrashEntriesCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly RestoreTrashEntriesCommandValidator Validator = new();

    /// <summary>A command every rule accepts, so a refusal below is the changed member's.</summary>
    private static readonly RestoreTrashEntriesCommand Valid = new(Guid.NewGuid(), ["1-0123456789abcdef"]);

    /// <summary>Lists the codes a validation result refuses with.</summary>
    /// <param name="result">The result.</param>
    /// <returns>The error messages, which are the panel's codes.</returns>
    private static List<string> CodesOf(ValidationResult result)
    {
        return result.Errors.Select(failure =>
        {
            return failure.ErrorMessage;
        }).ToList();
    }

    /// <summary>The unchanged command is accepted, so the refusals below are not a rule that refuses everything.</summary>
    [Fact]
    public void A_well_formed_command_is_valid()
    {
        Assert.True(Validator.Validate(Valid).IsValid);
    }

    /// <summary>A command with no account is refused.</summary>
    [Fact]
    public void A_command_with_no_account_is_refused()
    {
        Assert.False(Validator.Validate(Valid with { AccountId = Guid.Empty }).IsValid);
    }

    /// <summary>A batch naming nothing is refused as empty.</summary>
    [Fact]
    public void A_batch_naming_nothing_is_refused_as_empty()
    {
        var result = Validator.Validate(Valid with { TrashIds = [] });

        Assert.False(result.IsValid);
        Assert.Contains("FilesBatchEmpty", CodesOf(result));
    }

    /// <summary>An id that is not the shape the agent mints is refused, so it can never be read as a path.</summary>
    /// <param name="id">A refused trash id.</param>
    [Theory]
    [InlineData("../x")]
    [InlineData("1-xyz")]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("1-0123456789abcdef-extra")]
    public void An_id_that_is_not_a_trash_id_is_refused(string id)
    {
        var result = Validator.Validate(Valid with { TrashIds = [id] });

        Assert.False(result.IsValid);
        Assert.Contains("FilesPathInvalid", CodesOf(result));
    }
}
