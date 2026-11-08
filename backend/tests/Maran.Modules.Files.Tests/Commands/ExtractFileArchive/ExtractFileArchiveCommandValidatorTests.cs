using FluentValidation.Results;
using Maran.Modules.Files.Commands.ExtractFileArchive;
using Maran.Modules.Files.Tests.Validators;

namespace Maran.Modules.Files.Tests.Commands.ExtractFileArchive;

/// <summary>
/// Holds <see cref="ExtractFileArchiveCommandValidator"/> to its contract for extracting an archive: every path goes through the one
/// path rule, the trash directory is reserved, and each refusal carries the code the customer is shown.
/// </summary>
public sealed class ExtractFileArchiveCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly ExtractFileArchiveCommandValidator Validator = new();

    /// <summary>A command every rule accepts, so a refusal below is the changed member's.</summary>
    private static readonly ExtractFileArchiveCommand Valid = new(Guid.NewGuid(), "backups/site.zip", "restored");

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

    /// <summary>A hostile ArchivePath is refused as an invalid path.</summary>
    /// <param name="path">A hostile path.</param>
    [Theory]
    [MemberData(nameof(FilesPathRuleTests.Hostile), MemberType = typeof(FilesPathRuleTests))]
    public void A_hostile_archivepath_is_refused_as_an_invalid_path(string path)
    {
        var result = Validator.Validate(Valid with { ArchivePath = path });

        Assert.False(result.IsValid);
        Assert.Contains("FilesPathInvalid", CodesOf(result));
    }

    /// <summary>An empty ArchivePath names no entry and is refused.</summary>
    [Fact]
    public void An_empty_archivepath_is_refused()
    {
        Assert.False(Validator.Validate(Valid with { ArchivePath = string.Empty }).IsValid);
    }

    /// <summary>A ArchivePath inside the trash directory is refused as reserved.</summary>
    /// <param name="path">A path in the trash directory.</param>
    [Theory]
    [InlineData(".maran-trash")]
    [InlineData(".maran-trash/entries/x")]
    public void A_archivepath_in_the_trash_directory_is_refused_as_reserved(string path)
    {
        var result = Validator.Validate(Valid with { ArchivePath = path });

        Assert.False(result.IsValid);
        Assert.Contains("FilesPathReserved", CodesOf(result));
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

    /// <summary>A name that is not a recognised archive format is refused.</summary>
    [Fact]
    public void A_name_that_is_not_a_recognised_archive_format_is_refused()
    {
        var result = Validator.Validate(Valid with { ArchivePath = "notes.txt" });

        Assert.False(result.IsValid);
        Assert.Contains("FilesArchiveFormatUnknown", CodesOf(result));
    }

    /// <summary>The gzipped tar spellings are recognised.</summary>
    /// <param name="name">An archive name.</param>
    [Theory]
    [InlineData("site.tar.gz")]
    [InlineData("site.tgz")]
    [InlineData("SITE.ZIP")]
    public void The_recognised_archive_spellings_are_accepted(string name)
    {
        Assert.True(Validator.Validate(Valid with { ArchivePath = name }).IsValid);
    }
}
