using FluentValidation.Results;
using Maran.Modules.Files.Commands.DeleteFileEntries;
using Maran.Modules.Files.Tests.Validators;

namespace Maran.Modules.Files.Tests.Commands.DeleteFileEntries;

/// <summary>
/// Holds <see cref="DeleteFileEntriesCommandValidator"/> to its contract for deleting entries for good: every path goes through the one
/// path rule, the trash directory is reserved, and each refusal carries the code the customer is shown.
/// </summary>
public sealed class DeleteFileEntriesCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly DeleteFileEntriesCommandValidator Validator = new();

    /// <summary>A command every rule accepts, so a refusal below is the changed member's.</summary>
    private static readonly DeleteFileEntriesCommand Valid = new(Guid.NewGuid(), ["a.txt"], true);

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

    /// <summary>A hostile Paths is refused as an invalid path.</summary>
    /// <param name="path">A hostile path.</param>
    [Theory]
    [MemberData(nameof(FilesPathRuleTests.Hostile), MemberType = typeof(FilesPathRuleTests))]
    public void A_hostile_paths_is_refused_as_an_invalid_path(string path)
    {
        var result = Validator.Validate(Valid with { Paths = [path] });

        Assert.False(result.IsValid);
        Assert.Contains("FilesPathInvalid", CodesOf(result));
    }

    /// <summary>An empty Paths names no entry and is refused.</summary>
    [Fact]
    public void An_empty_paths_is_refused()
    {
        Assert.False(Validator.Validate(Valid with { Paths = [string.Empty] }).IsValid);
    }

    /// <summary>A Paths inside the trash directory is refused as reserved.</summary>
    /// <param name="path">A path in the trash directory.</param>
    [Theory]
    [InlineData(".maran-trash")]
    [InlineData(".maran-trash/entries/x")]
    public void A_paths_in_the_trash_directory_is_refused_as_reserved(string path)
    {
        var result = Validator.Validate(Valid with { Paths = [path] });

        Assert.False(result.IsValid);
        Assert.Contains("FilesPathReserved", CodesOf(result));
    }

    /// <summary>A batch naming nothing is refused as empty.</summary>
    [Fact]
    public void A_batch_naming_nothing_is_refused_as_empty()
    {
        var result = Validator.Validate(Valid with { Paths = [] });

        Assert.False(result.IsValid);
        Assert.Contains("FilesBatchEmpty", CodesOf(result));
    }

    /// <summary>A permanent delete that was not confirmed is refused.</summary>
    [Fact]
    public void A_delete_that_was_not_confirmed_is_refused()
    {
        var result = Validator.Validate(Valid with { Confirm = false });

        Assert.False(result.IsValid);
        Assert.Contains("FilesDeleteNotConfirmed", CodesOf(result));
    }
}
