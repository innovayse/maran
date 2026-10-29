namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The version a PostgreSQL server reported through <c>SHOW server_version_num</c>, parsed once.
/// </summary>
/// <remarks>
/// It is the vacuity guard of the version matrix (issue #98), kept free of any container so the
/// refusing branch can be proved without starting a second one. A leg labelled "PostgreSQL 13" that
/// silently ran 16 would pass every test it was created to fail, and a green gate over the wrong
/// server is read as evidence — so <see cref="Require"/> refuses when the server disagrees.
/// </remarks>
public sealed record PostgresServerVersion
{
    /// <summary>
    /// The first <c>server_version_num</c> of the two-part numbering PostgreSQL adopted at version 10:
    /// <c>major * 10000 + minor</c>. Older servers (<c>90600</c>) encode the major differently and are
    /// not supported by this panel.
    /// </summary>
    private const int FirstTwoPartNumber = 100000;

    /// <summary>The divisor that turns a <c>server_version_num</c> into its major version.</summary>
    private const int MajorDivisor = 10000;

    /// <summary>The number the server reported, for example <c>130014</c>.</summary>
    public int Number { get; }

    /// <summary>The major version, for example <c>13</c>.</summary>
    public int Major { get; }

    /// <summary>Wraps an already validated <c>server_version_num</c>.</summary>
    /// <param name="number">A number of at least <see cref="FirstTwoPartNumber"/>.</param>
    private PostgresServerVersion(int number)
    {
        Number = number;
        Major = number / MajorDivisor;
    }

    /// <summary>Parses the text <c>SHOW server_version_num</c> returns.</summary>
    /// <param name="serverVersionNum">The server's answer, for example <c>130014</c> or <c>160004</c>.</param>
    /// <returns>The parsed version.</returns>
    /// <exception cref="InvalidOperationException">The text is not a number a PostgreSQL 10+ server reports.</exception>
    public static PostgresServerVersion Parse(string serverVersionNum)
    {
        if (!int.TryParse(serverVersionNum, out var number) || number < FirstTwoPartNumber)
        {
            throw new InvalidOperationException(
                $"'{serverVersionNum}' is not a server_version_num of a PostgreSQL 10 or newer server.");
        }

        return new PostgresServerVersion(number);
    }

    /// <summary>Fails loudly when this server's major is not the one the run declared it expects.</summary>
    /// <param name="expectedMajor">The declared major, or <see langword="null"/> when the run makes no claim.</param>
    /// <exception cref="InvalidOperationException">The server's major differs from the declared one.</exception>
    public void Require(int? expectedMajor)
    {
        if (expectedMajor is not null && expectedMajor != Major)
        {
            throw new InvalidOperationException(
                $"This run declared PostgreSQL {expectedMajor} but the server it started is {Major} " +
                $"(server_version_num {Number}). The leg measured the wrong server, so its result is void.");
        }
    }
}
