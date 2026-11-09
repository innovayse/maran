using FluentValidation.Results;
using Maran.Modules.Files.Commands.MoveFileEntries;
using Maran.Modules.Files.Tests.Validators;

namespace Maran.Modules.Files.Tests.Commands.MoveFileEntries;

/// <summary>
/// Holds <see cref="MoveFileEntriesCommandValidator"/> to its contract for moving entries: every path goes through the one
/// path rule, the trash directory is reserved, and each refusal carries the code the customer is shown.
/// </summary>
public sealed class MoveFileEntriesCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly MoveFileEntriesCommandValidator Validator = new();

    /// <summary>A command every rule accepts, so a refusal below is the changed member's.</summary>
    private static readonly MoveFileEntriesCommand Valid = new(Guid.NewGuid(), ["a.txt"], "target");

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

    /// <summary>A hostile SourcePaths is refused as an invalid path.</summary>
    /// <param name="path">A hostile path.</param>
    [Theory]
    [MemberData(nameof(FilesPathRuleTests.Hostile), MemberType = typeof(FilesPathRuleTests))]
    public void A_hostile_sourcepaths_is_refused_as_an_invalid_path(string path)
    {
        var result = Validator.Validate(Valid with { SourcePaths = [path] });

        Assert.False(result.IsValid);
        Assert.Contains("FilesPathInvalid", CodesOf(result));
    }

    /// <summary>An empty SourcePaths names no entry and is refused.</summary>
    [Fact]
    public void An_empty_sourcepaths_is_refused()
    {
        Assert.False(Validator.Validate(Valid with { SourcePaths = [string.Empty] }).IsValid);
    }

    /// <summary>A SourcePaths inside the trash directory is refused as reserved.</summary>
    /// <param name="path">A path in the trash directory.</param>
    [Theory]
    [InlineData(".maran-trash")]
    [InlineData(".maran-trash/entries/x")]
    public void A_sourcepaths_in_the_trash_directory_is_refused_as_reserved(string path)
    {
        var result = Validator.Validate(Valid with { SourcePaths = [path] });

        Assert.False(result.IsValid);
        Assert.Contains("FilesPathReserved", CodesOf(result));
    }

    /// <summary>A batch naming nothing is refused as empty.</summary>
    [Fact]
    public void A_batch_naming_nothing_is_refused_as_empty()
    {
        var result = Validator.Validate(Valid with { SourcePaths = [] });

        Assert.False(result.IsValid);
        Assert.Contains("FilesBatchEmpty", CodesOf(result));
    }

    /// <summary>A hostile DestinationDirectory is refused as an invalid path.</summary>
    /// <param name="path">A hostile path.</param>
    [Theory]
    [MemberData(nameof(FilesPathRuleTests.Hostile), MemberType = typeof(FilesPathRuleTests))]
    public void A_hostile_destinationdirectory_is_refused_as_an_invalid_path(string path)
    {
        var result = Validator.Validate(Valid with { DestinationDirectory = path });

        Assert.False(result.IsValid);
        Assert.Contains("FilesPathInvalid", CodesOf(result));
    }

    /// <summary>The home itself is a legal DestinationDirectory.</summary>
    [Fact]
    public void The_home_is_a_legal_destinationdirectory()
    {
        Assert.True(Validator.Validate(Valid with { DestinationDirectory = string.Empty }).IsValid);
    }

    /// <summary>A DestinationDirectory inside the trash directory is refused as reserved.</summary>
    [Fact]
    public void A_destinationdirectory_in_the_trash_directory_is_refused_as_reserved()
    {
        var result = Validator.Validate(Valid with { DestinationDirectory = ".maran-trash/entries" });

        Assert.False(result.IsValid);
        Assert.Contains("FilesPathReserved", CodesOf(result));
    }

    /// <summary>A DestinationDirectory beneath one of the sources is refused as inside the source, and a sibling whose name only starts the same way is not.</summary>
    [Fact]
    public void A_destinationdirectory_inside_a_source_is_refused()
    {
        var inside = Validator.Validate(Valid with { SourcePaths = ["docs"], DestinationDirectory = "docs/old" });
        var same = Validator.Validate(Valid with { SourcePaths = ["docs"], DestinationDirectory = "docs" });
        var sibling = Validator.Validate(Valid with { SourcePaths = ["docs"], DestinationDirectory = "docs-old" });

        Assert.Contains("FilesDestinationInsideSource", CodesOf(inside));
        Assert.Contains("FilesDestinationInsideSource", CodesOf(same));
        Assert.True(sibling.IsValid);
    }
}
