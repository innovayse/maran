using System.Text.RegularExpressions;
using Maran.Host.Configuration;

namespace Maran.Host.Tests.Configuration;

/// <summary>
/// Behavioral contract of <see cref="DatabaseOptions"/>'s shipped default role name: the panel
/// that is started without <c>Database__Username</c> in its environment must still ask PostgreSQL
/// for the role the installer created, which is the service account's own name.
/// </summary>
/// <remarks>
/// This is pinned because the default is normally invisible. On a real server
/// <c>/etc/maran/panel.env</c> sets <c>Database__Username</c> and overrides whatever is compiled
/// in, so a wrong default costs nothing until the day the panel is started without that file — a
/// recovery boot, a container, a developer reading the shipped <c>appsettings.json</c> — and then
/// it fails at connect time with a role that does not exist. The default was left at
/// <c>panel</c> for exactly that reason when the service account was renamed to <c>maran</c>, and
/// nothing observed it.
///
/// The second assertion is what makes this more than a restatement of the source line. Production
/// authenticates over the unix socket by peer authentication, so the PostgreSQL role name is not a
/// free choice: <c>installer/lib/30-postgresql.sh</c> sets <c>MARAN_DB_ROLE="$MARAN_USER"</c>, and
/// <c>MARAN_USER</c> is the one authority in <c>installer/install.sh</c>. Reading that file is how
/// this test observes the thing it reports on rather than agreeing with itself; if the installer
/// renames the account again, this test names the file that has to change with it.
/// </remarks>
public sealed class DatabaseOptionsTests
{
    /// <summary>The shipped default role is the service account the installer creates.</summary>
    [Fact]
    public void The_default_database_role_is_the_service_account_name()
    {
        var options = new DatabaseOptions();

        Assert.Equal("maran", options.Username);
        Assert.Contains("Username=maran", options.BuildConnectionString(), StringComparison.Ordinal);
    }

    /// <summary>The shipped default matches the account name the installer's one authority sets.</summary>
    [Fact]
    public void The_default_database_role_matches_the_installers_service_account()
    {
        var installer = Path.Combine(RepositoryRoot(), "installer", "install.sh");
        Assert.True(File.Exists(installer), $"installer/install.sh was not found at {installer}; this test could observe nothing.");

        var match = Regex.Match(
            File.ReadAllText(installer),
            @"^MARAN_USER=(?<name>\S+)$",
            RegexOptions.Multiline,
            TimeSpan.FromSeconds(5));
        Assert.True(match.Success, "installer/install.sh no longer declares MARAN_USER=<name>; this test could observe nothing.");

        Assert.Equal(match.Groups["name"].Value, new DatabaseOptions().Username);
    }

    /// <summary>Walks up from the test binary to the folder holding the solution.</summary>
    /// <returns>Absolute path of the repository root.</returns>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "backend", "Maran.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root was not found above the test output directory.");
    }
}
