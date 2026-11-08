using FluentValidation.Results;
using Maran.Modules.Files.Commands.CreateFileArchive;
using Maran.Modules.Files.Domain.Enums;
using Maran.Modules.Files.Tests.Validators;

namespace Maran.Modules.Files.Tests.Commands.CreateFileArchive;

/// <summary>
/// Holds <see cref="CreateFileArchiveCommandValidator"/> to its contract for archiving entries: every path goes through the one
/// path rule, the trash directory is reserved, and each refusal carries the code the customer is shown.
/// </summary>
public sealed class CreateFileArchiveCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly CreateFileArchiveCommandValidator Validator = new();

    /// <summary>A command every rule accepts, so a refusal below is the changed member's.</summary>
    private static readonly CreateFileArchiveCommand Valid = new(Guid.NewGuid(), ["a"], "backups/site.zip", ArchiveFormatKind.Zip);

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

    /// <summary>A hostile DestinationPath is refused as an invalid path.</summary>
    /// <param name="path">A hostile path.</param>
    [Theory]
    [MemberData(nameof(FilesPathRuleTests.Hostile), MemberType = typeof(FilesPathRuleTests))]
    public void A_hostile_destinationpath_is_refused_as_an_invalid_path(string path)
    {
        var result = Validator.Validate(Valid with { DestinationPath = path });

        Assert.False(result.IsValid);
        Assert.Contains("FilesPathInvalid", CodesOf(result));
    }

    /// <summary>An empty DestinationPath names no entry and is refused.</summary>
    [Fact]
    public void An_empty_destinationpath_is_refused()
    {
        Assert.False(Validator.Validate(Valid with { DestinationPath = string.Empty }).IsValid);
    }

    /// <summary>A DestinationPath inside the trash directory is refused as reserved.</summary>
    /// <param name="path">A path in the trash directory.</param>
    [Theory]
    [InlineData(".maran-trash")]
    [InlineData(".maran-trash/entries/x")]
    public void A_destinationpath_in_the_trash_directory_is_refused_as_reserved(string path)
    {
        var result = Validator.Validate(Valid with { DestinationPath = path });

        Assert.False(result.IsValid);
        Assert.Contains("FilesPathReserved", CodesOf(result));
    }

    /// <summary>An archive name that does not carry its format's suffix is refused.</summary>
    [Fact]
    public void An_archive_name_that_does_not_carry_its_formats_suffix_is_refused()
    {
        var result = Validator.Validate(Valid with { DestinationPath = "backups/site.tar.gz" });

        Assert.False(result.IsValid);
        Assert.Contains("FilesArchiveFormatUnknown", CodesOf(result));
    }

    /// <summary>A gzipped tar named for its format is accepted.</summary>
    [Fact]
    public void A_gzipped_tar_named_for_its_format_is_accepted()
    {
        var command = Valid with { DestinationPath = "backups/site.tar.gz", Format = ArchiveFormatKind.TarGz };

        Assert.True(Validator.Validate(command).IsValid);
    }

    /// <summary>A format the module does not know is refused.</summary>
    [Fact]
    public void A_format_the_module_does_not_know_is_refused()
    {
        var result = Validator.Validate(Valid with { Format = (ArchiveFormatKind)99 });

        Assert.False(result.IsValid);
        Assert.Contains("FilesArchiveFormatUnknown", CodesOf(result));
    }
}
