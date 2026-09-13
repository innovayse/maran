using Maran.Modules.Cron.Queries.GetCronEntryOutput;

namespace Maran.Modules.Cron.Tests.Queries.GetCronEntryOutput;

/// <summary>
/// What the operation that reads an entry's last run accepts: the shared identifier rule, bound to
/// a query as firmly as to a command.
/// </summary>
/// <remarks>
/// This is the read whose identifier the agent turns into a path under the account's home, so it is
/// the one place a malformed id would name a file rather than a row. A query without a validator
/// would be the gap.
/// </remarks>
public sealed class GetCronEntryOutputQueryValidatorTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    /// <summary>A lowercase hyphenated uuid is accepted.</summary>
    [Fact]
    public void A_lowercase_hyphenated_uuid_is_accepted()
    {
        Assert.True(IsValid("3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d"));
    }

    /// <summary>An identifier shaped like a path is refused before it can reach the agent.</summary>
    [Theory]
    [InlineData("../../../etc/cron.d/evil")]
    [InlineData("/etc/cron.d/evil")]
    [InlineData("3F1A5B7C-0D2E-4A6B-8C9D-0E1F2A3B4C5D")]
    [InlineData("3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d\n")]
    [InlineData("")]
    public void An_identifier_shaped_like_a_path_is_refused_before_it_can_reach_the_agent(string entryId)
    {
        Assert.False(IsValid(entryId));
    }

    /// <summary>Runs the validator over one read.</summary>
    /// <param name="entryId">The identifier to check.</param>
    /// <returns>Whether the validator accepted the query.</returns>
    private static bool IsValid(string entryId)
    {
        return new GetCronEntryOutputQueryValidator()
            .Validate(new GetCronEntryOutputQuery(AccountId, entryId))
            .IsValid;
    }
}
