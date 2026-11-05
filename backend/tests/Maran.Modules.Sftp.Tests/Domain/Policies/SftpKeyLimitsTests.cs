using System.Globalization;
using System.Text.RegularExpressions;
using Maran.Modules.Sftp.Domain.Policies;
using Maran.Modules.Sftp.Domain.ValueObjects;

namespace Maran.Modules.Sftp.Tests.Domain.Policies;

/// <summary>
/// That the panel's key limits are the agent's own numbers, read from the agent's source, so a change
/// to either side fails here instead of surfacing as a refused key set on a real host.
/// </summary>
public sealed class SftpKeyLimitsTests
{
    /// <summary>The agent source that declares the most keys one login may hold.</summary>
    private static readonly string[] KeySetSource =
        ["agent", "crates", "ops", "src", "sftp", "model", "authorized_key_set.rs"];

    /// <summary>The agent source that declares the longest key text.</summary>
    private static readonly string[] KeySource =
        ["agent", "crates", "agent-core", "src", "validation", "system", "ssh_public_key.rs"];

    /// <summary>The panel allows exactly the number of keys per login the agent accepts in one set.</summary>
    [Fact]
    public void The_panel_allows_exactly_the_number_of_keys_per_login_the_agent_accepts()
    {
        var declaration = ReadDeclaration(KeySetSource, @"pub const MAX_KEYS: usize = (\d+);");

        Assert.Equal(SftpKeyLimits.MaxPerLogin, int.Parse(declaration.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>The panel refuses key text over exactly the length the agent refuses.</summary>
    [Fact]
    public void The_panel_refuses_key_text_over_exactly_the_length_the_agent_refuses()
    {
        var declaration = ReadDeclaration(KeySource, @"pub const MAX_LENGTH: usize = (\d+) \* (\d+);");

        var agentLimit = int.Parse(declaration.Groups[1].Value, CultureInfo.InvariantCulture)
            * int.Parse(declaration.Groups[2].Value, CultureInfo.InvariantCulture);
        Assert.Equal(SftpPublicKey.MaxLength, agentLimit);
    }

    /// <summary>The label limit is sixty-four characters, the size the column and the screens are made for.</summary>
    [Fact]
    public void The_label_limit_is_sixty_four_characters()
    {
        Assert.Equal(64, SftpKeyLimits.MaxLabelLength);
    }

    /// <summary>Finds the agent's declaration of a constant, failing loudly when it has moved.</summary>
    /// <param name="segments">The path of the agent source file under the repository root.</param>
    /// <param name="pattern">A regular expression matching the declaration, with its numbers captured.</param>
    /// <returns>The match.</returns>
    private static Match ReadDeclaration(string[] segments, string pattern)
    {
        var match = Regex.Match(File.ReadAllText(PathOf(segments)), pattern);

        // A declaration that cannot be found is a failure, never a pass over an empty answer: the two
        // tests above compare against what this finds, so a blind search would make them decoration.
        Assert.True(match.Success, "The agent's declaration was not found: " + pattern);

        return match;
    }

    /// <summary>Resolves a path under the repository root.</summary>
    /// <param name="segments">The path segments below the repository root.</param>
    /// <returns>The absolute path.</returns>
    private static string PathOf(string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "backend", "Maran.sln")))
            {
                return Path.Combine(segments.Prepend(directory.FullName).ToArray());
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root was not found above the test output directory.");
    }
}
