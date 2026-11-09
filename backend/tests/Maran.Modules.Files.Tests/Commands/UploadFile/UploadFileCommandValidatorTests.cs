using Maran.Modules.Files.Commands.UploadFile;
using Maran.Modules.Files.Tests.TestSupport;

namespace Maran.Modules.Files.Tests.Commands.UploadFile;

/// <summary>Holds <see cref="UploadFileCommandValidator"/> to the one path rule and to an account being named.</summary>
public sealed class UploadFileCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly UploadFileCommandValidator Validator = new();

    /// <summary>Builds the request with a path.</summary>
    /// <param name="path">The path to put in it.</param>
    /// <returns>The request.</returns>
    private static UploadFileCommand Request(string path)
    {
        return new UploadFileCommand(Guid.NewGuid(), path);
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
}
