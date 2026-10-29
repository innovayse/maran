namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Which PostgreSQL server a test run was asked to measure: the Docker image to start and, when the run
/// makes the claim, the major version that image must turn out to be.
/// </summary>
/// <remarks>
/// It is read from the environment once, so one suite can be measured against more than one server
/// (issue #98: the installer supports distributions that ship PostgreSQL 13 and 16). The image comes
/// from <see cref="ImageVariable"/>; the major from <see cref="MajorVariable"/>. With neither set the
/// run uses <see cref="DefaultImage"/> and makes no claim, so a local run behaves as it always did.
/// </remarks>
/// <param name="Image">The Docker image to run, for example <c>postgres:13-alpine</c>.</param>
/// <param name="ExpectedMajor">
/// The major the started server must report, or <see langword="null"/> when the run makes no claim.
/// </param>
public sealed record PostgresTestTarget(string Image, int? ExpectedMajor)
{
    /// <summary>The environment variable naming the Docker image to run, for example <c>postgres:13-alpine</c>.</summary>
    public const string ImageVariable = "MARAN_TEST_PG_IMAGE";

    /// <summary>The environment variable declaring the PostgreSQL major the run must be measuring.</summary>
    public const string MajorVariable = "MARAN_TEST_PG_MAJOR";

    /// <summary>The image used when <see cref="ImageVariable"/> is not set.</summary>
    public const string DefaultImage = "postgres:16-alpine";

    /// <summary>Reads the target from <see cref="ImageVariable"/> and <see cref="MajorVariable"/>.</summary>
    /// <returns>The target this process was asked to measure.</returns>
    /// <exception cref="InvalidOperationException">The declared major is not a number.</exception>
    public static PostgresTestTarget FromEnvironment()
    {
        return Parse(
            Environment.GetEnvironmentVariable(ImageVariable),
            Environment.GetEnvironmentVariable(MajorVariable));
    }

    /// <summary>Builds a target from the raw text of the two variables.</summary>
    /// <param name="image">The image text, or <see langword="null"/>/blank for <see cref="DefaultImage"/>.</param>
    /// <param name="major">The declared major text, or <see langword="null"/>/blank when no claim is made.</param>
    /// <returns>The target the text describes.</returns>
    /// <exception cref="InvalidOperationException">The declared major is not a number.</exception>
    public static PostgresTestTarget Parse(string? image, string? major)
    {
        var resolvedImage = string.IsNullOrWhiteSpace(image) ? DefaultImage : image;

        if (string.IsNullOrWhiteSpace(major))
        {
            return new PostgresTestTarget(resolvedImage, null);
        }

        if (!int.TryParse(major, out var expected))
        {
            throw new InvalidOperationException($"The declared PostgreSQL major '{major}' is not a number.");
        }

        return new PostgresTestTarget(resolvedImage, expected);
    }
}
