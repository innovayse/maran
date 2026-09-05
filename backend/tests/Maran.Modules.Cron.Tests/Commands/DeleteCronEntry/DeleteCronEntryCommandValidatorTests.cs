using Maran.Modules.Cron.Commands.DeleteCronEntry;

namespace Maran.Modules.Cron.Tests.Commands.DeleteCronEntry;

/// <summary>What this panel accepts as a cron entry identifier, on the operation that removes one.</summary>
public sealed class DeleteCronEntryCommandValidatorTests
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
    [InlineData("..")]
    [InlineData("3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d/../../x")]
    public void An_identifier_shaped_like_a_path_is_refused_before_it_can_reach_the_agent(string entryId)
    {
        // The agent turns this id into three paths under the account's home, and a path join with an
        // absolute string REPLACES what it is joined to. The agent refuses these itself; refusing
        // them here too means the panel is never the layer that widened what the agent narrowed.
        Assert.False(IsValid(entryId));
    }

    /// <summary>A uuid in any other spelling is refused because an id has one text.</summary>
    [Theory]
    [InlineData("3F1A5B7C-0D2E-4A6B-8C9D-0E1F2A3B4C5D")]
    [InlineData("3f1a5b7c0d2e4a6b8c9d0e1f2a3b4c5d")]
    [InlineData("{3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d}")]
    [InlineData("urn:uuid:3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d")]
    [InlineData("3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5")]
    [InlineData("")]
    public void A_uuid_in_any_other_spelling_is_refused_because_an_id_has_one_text(string entryId)
    {
        // Deliberately not parsed as a Guid, which would accept every one of these and re-emit a
        // canonical spelling: the id the agent minted is the id the agent stores, and quietly
        // accepting another spelling of it hides from a caller that they sent one.
        Assert.False(IsValid(entryId));
    }

    /// <summary>An identifier with a trailing newline is refused.</summary>
    [Fact]
    public void An_identifier_with_a_trailing_newline_is_refused()
    {
        // The reason the pattern is anchored with \z rather than $: in .NET a `$` also matches
        // immediately before a trailing newline, so this exact value would satisfy a `$`-anchored
        // pattern — and a newline in a value bound for a file path is what the rule exists to refuse.
        Assert.False(IsValid("3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d\n"));
    }

    /// <summary>Runs the validator over one identifier.</summary>
    /// <param name="entryId">The identifier to check.</param>
    /// <returns>Whether the validator accepted the command.</returns>
    private static bool IsValid(string entryId)
    {
        return new DeleteCronEntryCommandValidator()
            .Validate(new DeleteCronEntryCommand(AccountId, entryId, "203.0.113.7", "tests"))
            .IsValid;
    }
}
