using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Domain.Policies;

namespace Maran.ArchitectureTests;

/// <summary>
/// Keeps the API's account-name rules equal to the agent's, which is the only control that can:
/// the two lists live in two languages and nothing compiles one against the other. The agent's
/// managed-account type is the boundary that protects the host; the API copy only gives an early,
/// localized refusal, so a name the agent reserves and the API does not is accepted and then fails
/// after the request was taken. The Rust source is read as text, the same way
/// <see cref="SpaLocaleKeys"/> reads the SPA bundles.
/// </summary>
public sealed class AccountNameLockstepTests
{
    /// <summary>The agent source that holds the reserved-name list, relative to the repository root.</summary>
    private static readonly string[] ManagedAccountPath =
        ["agent", "crates", "agent-core", "src", "validation", "system", "managed_account.rs"];

    /// <summary>The agent source that holds the account-name alphabet and bounds.</summary>
    private static readonly string[] AccountNamePath =
        ["agent", "crates", "agent-core", "src", "validation", "system", "name.rs"];

    /// <summary>How many names the threat note's table holds; a parse that finds fewer has gone blind.</summary>
    private const int ExpectedNameCount = 103;

    /// <summary>Every name the agent reserves is reserved by the API too, and the two sets are equal.</summary>
    [Fact]
    public void Every_name_the_agent_reserves_is_reserved_by_the_api()
    {
        var agentNames = AgentReservedNames();

        // Vacuity guards: a regex that matched nothing would make the subset check pass forever.
        Assert.Equal(ExpectedNameCount, agentNames.Count);
        Assert.Contains("root", agentNames);

        var missingFromApi = agentNames.Where(name =>
        {
            return !ReservedAccountNamePolicy.IsReserved(name);
        }).ToList();
        Assert.True(
            missingFromApi.Count == 0,
            $"Reserved by the agent but accepted by the API: {string.Join(", ", missingFromApi)}");
        Assert.True(
            agentNames.SetEquals(ReservedAccountNamePolicy.ExactNames),
            "The API reserves names the agent does not; the two lists must be the threat note's table entry for entry.");
    }

    /// <summary>The positive control: the parse really reads this file, so a name added there would show up here.</summary>
    [Fact]
    public void The_parse_sees_a_name_that_is_not_reserved_as_absent()
    {
        var agentNames = AgentReservedNames();

        Assert.DoesNotContain("alice", agentNames);
        Assert.False(ReservedAccountNamePolicy.IsReserved("alice"));
    }

    /// <summary>The API's alphabet pattern allows exactly the lengths the agent's constants allow.</summary>
    [Fact]
    public void The_api_alphabet_has_the_same_bounds_as_the_agent()
    {
        var source = File.ReadAllText(PathUnderRoot(AccountNamePath));
        var minimum = int.Parse(
            Regex.Match(source, @"const MIN_LENGTH: usize = (\d+);").Groups[1].Value,
            CultureInfo.InvariantCulture);
        var maximum = int.Parse(
            Regex.Match(source, @"const MAX_LENGTH: usize = (\d+);").Groups[1].Value,
            CultureInfo.InvariantCulture);
        var field = typeof(CreateAccountCommandValidator).GetField(
            "LinuxUserNamePattern",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var pattern = new Regex((string)field.GetRawConstantValue()!, RegexOptions.CultureInvariant);

        Assert.Equal(3, minimum);
        Assert.Matches(pattern, new string('a', minimum));
        Assert.DoesNotMatch(pattern, new string('a', minimum - 1));
        Assert.Matches(pattern, new string('a', maximum));
        Assert.DoesNotMatch(pattern, new string('a', maximum + 1));
        Assert.DoesNotMatch(pattern, "www-data");
        Assert.DoesNotMatch(pattern, "acme\n");
        Assert.Matches(pattern, "a_b9");
    }

    /// <summary>Reads the names out of the agent's <c>RESERVED_NAMES</c> constant.</summary>
    /// <returns>The set of reserved names, without comments.</returns>
    private static HashSet<string> AgentReservedNames()
    {
        var source = File.ReadAllText(PathUnderRoot(ManagedAccountPath));
        var start = source.IndexOf("const RESERVED_NAMES", StringComparison.Ordinal);
        Assert.True(start >= 0, "RESERVED_NAMES was not found in managed_account.rs.");
        var end = source.IndexOf("];", start, StringComparison.Ordinal);
        var block = Regex.Replace(source[start..end], @"//[^\n]*", string.Empty);

        return Regex.Matches(block, "\"([^\"]+)\"")
            .Select(match =>
            {
                return match.Groups[1].Value;
            })
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Joins path segments onto the repository root.</summary>
    /// <param name="segments">Folder and file names below the root.</param>
    /// <returns>The absolute path.</returns>
    private static string PathUnderRoot(string[] segments)
    {
        return Path.Combine(segments.Prepend(SpaLocaleKeys.RepositoryRoot()).ToArray());
    }
}
