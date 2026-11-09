using FluentValidation.Results;
using Maran.Modules.Files.Commands.CreateFileEntry;
using Maran.Modules.Files.Domain.Enums;
using Maran.Modules.Files.Tests.Validators;

namespace Maran.Modules.Files.Tests.Commands.CreateFileEntry;

/// <summary>
/// Holds <see cref="CreateFileEntryCommandValidator"/> to its contract for creating an entry: every path goes through the one
/// path rule, the trash directory is reserved, and each refusal carries the code the customer is shown.
/// </summary>
public sealed class CreateFileEntryCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly CreateFileEntryCommandValidator Validator = new();

    /// <summary>A command every rule accepts, so a refusal below is the changed member's.</summary>
    private static readonly CreateFileEntryCommand Valid = new(Guid.NewGuid(), "docs/new.txt", NewFileEntryKind.File);

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

    /// <summary>A kind that is neither a file nor a folder is refused.</summary>
    [Fact]
    public void A_kind_that_is_neither_file_nor_folder_is_refused()
    {
        Assert.False(Validator.Validate(Valid with { Kind = (NewFileEntryKind)99 }).IsValid);
    }
}
