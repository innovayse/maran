using Maran.Modules.ServerMigration.Domain.Policies;

namespace Maran.Modules.ServerMigration.Tests.Domain.Policies;

/// <summary>Which source databases can be imported under the name they have (decision D1: no rename).</summary>
public sealed class ImportedDatabaseNamePolicyTests
{
    /// <summary>A database named for its owner keeps its name, and the suffix is what the Databases module creates.</summary>
    [Fact]
    public void A_database_prefixed_with_its_owner_gives_its_suffix()
    {
        Assert.Equal("shop", ImportedDatabaseNamePolicy.SuffixOf("alice", "alice_shop"));
        Assert.Equal("wp2", ImportedDatabaseNamePolicy.SuffixOf("alice_bob", "alice_bob_wp2"));
    }

    /// <summary>A name that is not the owner's, or whose suffix this panel never creates, is not importable.</summary>
    [Theory]
    [InlineData("alice", "bob_shop")]
    [InlineData("alice", "shop")]
    [InlineData("alice", "alice_")]
    [InlineData("alice", "alice_Shop")]
    [InlineData("alice", "alice_my_shop")]
    [InlineData("alice", "alice_sh-op")]
    [InlineData("alice", "alicia_shop")]
    public void Any_other_name_cannot_keep_its_name(string account, string database)
    {
        Assert.Null(ImportedDatabaseNamePolicy.SuffixOf(account, database));
    }

    /// <summary>A suffix past the longest this panel creates is refused rather than cut.</summary>
    [Fact]
    public void A_suffix_over_the_ceiling_is_refused()
    {
        Assert.NotNull(ImportedDatabaseNamePolicy.SuffixOf("alice", "alice_" + new string('a', 40)));
        Assert.Null(ImportedDatabaseNamePolicy.SuffixOf("alice", "alice_" + new string('a', 41)));
    }
}
