using FluentValidation.Results;
using Maran.Modules.Files.Commands.ChangeFilePermissions;
using Maran.Modules.Files.Tests.Validators;

namespace Maran.Modules.Files.Tests.Commands.ChangeFilePermissions;

/// <summary>
/// Holds <see cref="ChangeFilePermissionsCommandValidator"/> to its contract for changing permissions: every path goes through the one
/// path rule, the trash directory is reserved, and each refusal carries the code the customer is shown.
/// </summary>
public sealed class ChangeFilePermissionsCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly ChangeFilePermissionsCommandValidator Validator = new();

    /// <summary>A command every rule accepts, so a refusal below is the changed member's.</summary>
    private static readonly ChangeFilePermissionsCommand Valid = new(Guid.NewGuid(), "docs/a.txt", "644", false);

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

    /// <summary>A mode that is not three octal digits is refused, setuid and sticky bits included.</summary>
    /// <param name="mode">A refused mode.</param>
    [Theory]
    [InlineData("999")]
    [InlineData("4755")]
    [InlineData("rwx")]
    [InlineData("")]
    [InlineData("-1")]
    public void A_mode_that_is_not_plain_permission_bits_is_refused(string mode)
    {
        var result = Validator.Validate(Valid with { Mode = mode });

        Assert.False(result.IsValid);
        Assert.Contains("FilesModeInvalid", CodesOf(result));
    }

    /// <summary>Ordinary modes are accepted.</summary>
    /// <param name="mode">An accepted mode.</param>
    [Theory]
    [InlineData("600")]
    [InlineData("755")]
    [InlineData("777")]
    [InlineData("0")]
    public void An_ordinary_mode_is_accepted(string mode)
    {
        Assert.True(Validator.Validate(Valid with { Mode = mode }).IsValid);
    }

    /// <summary>A folder mode that is plain non-zero permission bits, or absent, is accepted.</summary>
    /// <param name="directoryMode">An accepted folder mode, or null for none.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("755")]
    [InlineData("700")]
    [InlineData("1")]
    public void An_ordinary_folder_mode_or_none_is_accepted(string? directoryMode)
    {
        Assert.True(Validator.Validate(Valid with { Recursive = true, DirectoryMode = directoryMode }).IsValid);
    }

    /// <summary>A folder mode that is not plain permission bits, or is zero, is refused as an invalid mode.</summary>
    /// <param name="directoryMode">A refused folder mode.</param>
    [Theory]
    [InlineData("999")]
    [InlineData("4755")]
    [InlineData("rwx")]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("000")]
    public void A_folder_mode_that_is_not_plain_nonzero_permission_bits_is_refused(string directoryMode)
    {
        var result = Validator.Validate(Valid with { Recursive = true, DirectoryMode = directoryMode });

        Assert.False(result.IsValid);
        Assert.Contains("FilesModeInvalid", CodesOf(result));
    }
}
