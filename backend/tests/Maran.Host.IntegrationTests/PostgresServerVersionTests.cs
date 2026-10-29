using Maran.Host.IntegrationTests.Fixtures;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Proves the version guard of the PostgreSQL matrix (issue #98) can both accept and refuse, and that
/// the server the fixture started is the one the run declared.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class PostgresServerVersionTests
{
    /// <summary>The server the assembly shares, whose reported version the positive control reads.</summary>
    private readonly PostgresFixture _postgres;

    /// <summary>Binds this class to the shared server.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public PostgresServerVersionTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    /// <summary>The started server reports the major the run declared, or is simply readable when none was.</summary>
    [Fact]
    public void Started_server_reports_the_major_the_run_declared()
    {
        var version = Assert.IsType<PostgresServerVersion>(_postgres.ServerVersion);

        if (_postgres.Target.ExpectedMajor is { } declared)
        {
            Assert.Equal(declared, version.Major);
        }
    }

    /// <summary>The major is read from both a PostgreSQL 13 and a PostgreSQL 16 version number.</summary>
    [Theory]
    [InlineData("130014", 13)]
    [InlineData("160004", 16)]
    public void Major_is_read_from_the_server_version_number(string serverVersionNum, int expected)
    {
        Assert.Equal(expected, PostgresServerVersion.Parse(serverVersionNum).Major);
    }

    /// <summary>A server of the declared major is accepted, as is a run that declares nothing.</summary>
    [Theory]
    [InlineData("130014", 13)]
    [InlineData("160004", 16)]
    [InlineData("160004", null)]
    public void Matching_or_undeclared_major_is_accepted(string serverVersionNum, int? declared)
    {
        PostgresServerVersion.Parse(serverVersionNum).Require(declared);
    }

    /// <summary>A server of a different major than declared is refused, in either direction, not passed.</summary>
    [Theory]
    [InlineData("160004", 13)]
    [InlineData("130014", 16)]
    public void A_server_of_a_different_major_than_declared_is_refused(string serverVersionNum, int declared)
    {
        var version = PostgresServerVersion.Parse(serverVersionNum);

        var error = Assert.Throws<InvalidOperationException>(() => { version.Require(declared); });

        Assert.Contains($"declared PostgreSQL {declared}", error.Message);
        Assert.Contains($"is {version.Major}", error.Message);
    }

    /// <summary>Text that is not a PostgreSQL 10+ version number is refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("90600")]
    public void A_version_number_that_cannot_be_read_is_refused(string serverVersionNum)
    {
        Assert.Throws<InvalidOperationException>(() => { PostgresServerVersion.Parse(serverVersionNum); });
    }

    /// <summary>A run with no variables set uses the default image and makes no claim about the major.</summary>
    [Fact]
    public void Unset_variables_give_the_default_image_and_no_declared_major()
    {
        var target = PostgresTestTarget.Parse(null, "");

        Assert.Equal(PostgresTestTarget.DefaultImage, target.Image);
        Assert.Null(target.ExpectedMajor);
    }

    /// <summary>A named image and a declared major are both carried into the target.</summary>
    [Fact]
    public void Named_image_and_major_are_carried_into_the_target()
    {
        var target = PostgresTestTarget.Parse("postgres:13-alpine", "13");

        Assert.Equal("postgres:13-alpine", target.Image);
        Assert.Equal(13, target.ExpectedMajor);
    }

    /// <summary>A declared major that is not a number is refused rather than silently ignored.</summary>
    [Fact]
    public void A_declared_major_that_is_not_a_number_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => { PostgresTestTarget.Parse(null, "thirteen"); });
    }
}
