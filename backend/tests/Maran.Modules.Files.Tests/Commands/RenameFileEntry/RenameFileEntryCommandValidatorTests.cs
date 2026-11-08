using FluentValidation.Results;
using Maran.Modules.Files.Commands.RenameFileEntry;
using Maran.Modules.Files.Tests.Validators;

namespace Maran.Modules.Files.Tests.Commands.RenameFileEntry;

/// <summary>
/// Holds <see cref="RenameFileEntryCommandValidator"/> to its contract for renaming an entry: every path goes through the one
/// path rule, the trash directory is reserved, and each refusal carries the code the customer is shown.
/// </summary>
public sealed class RenameFileEntryCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly RenameFileEntryCommandValidator Validator = new();

    /// <summary>A command every rule accepts, so a refusal below is the changed member's.</summary>
    private static readonly RenameFileEntryCommand Valid = new(Guid.NewGuid(), "docs/old.txt", "new.txt");

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

    /// <summary>A hostile Path is refused as an invalid path.</summary>
    /// <param name="path">A hostile path.</param>
    [Theory]
    [MemberData(nameof(FilesPathRuleTests.Hostile), MemberType = typeof(FilesPathRuleTests))]
    public void A_hostile_path_is_refused_as_an_invalid_path(string path)
    {
        var result = Validator.Validate(Valid with { Path = path });

        Assert.False(result.IsValid);
        Assert.Contains("FilesPathInvalid", CodesOf(result));
    }

    /// <summary>An empty Path names no entry and is refused.</summary>
    [Fact]
    public void An_empty_path_is_refused()
    {
        Assert.False(Validator.Validate(Valid with { Path = string.Empty }).IsValid);
    }

    /// <summary>A Path inside the trash directory is refused as reserved.</summary>
    /// <param name="path">A path in the trash directory.</param>
    [Theory]
    [InlineData(".maran-trash")]
    [InlineData(".maran-trash/entries/x")]
    public void A_path_in_the_trash_directory_is_refused_as_reserved(string path)
    {
        var result = Validator.Validate(Valid with { Path = path });

        Assert.False(result.IsValid);
        Assert.Contains("FilesPathReserved", CodesOf(result));
    }

    /// <summary>A new name that is a path, or empty, is refused: a rename never moves an entry.</summary>
    /// <param name="name">A refused new name.</param>
    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("x\0y")]
    public void A_new_name_that_is_not_one_plain_segment_is_refused(string name)
    {
        var result = Validator.Validate(Valid with { NewName = name });

        Assert.False(result.IsValid);
        Assert.Contains("FilesPathInvalid", CodesOf(result));
    }
}
