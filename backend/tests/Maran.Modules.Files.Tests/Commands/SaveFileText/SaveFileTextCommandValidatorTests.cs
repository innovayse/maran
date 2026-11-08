using Maran.Modules.Files.Commands.SaveFileText;
using Maran.Modules.Files.Tests.TestSupport;

namespace Maran.Modules.Files.Tests.Commands.SaveFileText;

/// <summary>Holds <see cref="SaveFileTextCommandValidator"/> to the one path rule and to an account being named.</summary>
public sealed class SaveFileTextCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly SaveFileTextCommandValidator Validator = new();

    /// <summary>Builds the request with a path.</summary>
    /// <param name="path">The path to put in it.</param>
    /// <returns>The request.</returns>
    private static SaveFileTextCommand Request(string path)
    {
        return new SaveFileTextCommand(Guid.NewGuid(), path, "text");
    }

    /// <summary>A path the rule refuses is refused, at the boundary, before any handler runs.</summary>
    /// <param name="path">A hostile path.</param>
    [Theory]
    [MemberData(nameof(FilesHostilePaths.Refused), MemberType = typeof(FilesHostilePaths))]
    public void A_hostile_path_is_refused(string path)
    {
        Assert.False(Validator.Validate(Request(path)).IsValid);
    }

    /// <summary>The control: a legal path is accepted, so a validator that refused everything would fail here.</summary>
    [Fact]
    public void A_legal_path_is_accepted()
    {
        Assert.True(Validator.Validate(Request(FilesHostilePaths.Legal)).IsValid);
    }

    /// <summary>The home is not a file, so the empty path is refused.</summary>
    [Fact]
    public void The_empty_path_is_refused()
    {
        Assert.False(Validator.Validate(Request(string.Empty)).IsValid);
    }

    /// <summary>An empty file is a legitimate save.</summary>
    [Fact]
    public void An_empty_text_is_accepted()
    {
        Assert.True(Validator.Validate(new SaveFileTextCommand(Guid.NewGuid(), FilesHostilePaths.Legal, string.Empty)).IsValid);
    }
}
