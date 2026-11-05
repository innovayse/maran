using Maran.Modules.Sftp.Domain.Entities;
using Maran.Modules.Sftp.Domain.ValueObjects;
using Maran.Modules.Sftp.Mappers;
using Maran.Modules.Sftp.Tests.TestSupport;

namespace Maran.Modules.Sftp.Tests.Mappers;

/// <summary>How a login's key rows become the complete set the agent is sent.</summary>
public sealed class SftpAuthorizedKeysMapperTests
{
    /// <summary>The instant the first key was registered.</summary>
    private static readonly DateTimeOffset Start = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The set carries each key as its bare one-line form, never a label or a fingerprint.</summary>
    [Fact]
    public void The_set_carries_each_key_as_its_bare_one_line_form()
    {
        var keys = Keys((SftpTestKeys.Ed25519 + " user@host", "laptop", 0));

        var set = SftpAuthorizedKeysMapper.From(keys);

        Assert.Equal([SftpTestKeys.Ed25519], set);
    }

    /// <summary>Keys are listed oldest first whatever order the rows arrive in.</summary>
    [Fact]
    public void Keys_are_listed_oldest_first_whatever_order_the_rows_arrive_in()
    {
        var keys = Keys(
            (SftpTestKeys.P384, "newest", 2),
            (SftpTestKeys.Ed25519, "oldest", 0),
            (SftpTestKeys.P256, "middle", 1));

        var set = SftpAuthorizedKeysMapper.From(keys);

        Assert.Equal([SftpTestKeys.Ed25519, SftpTestKeys.P256, SftpTestKeys.P384], set);
    }

    /// <summary>Keys registered at the same instant are ordered by identity, so the same rows always give the same list.</summary>
    [Fact]
    public void Keys_registered_at_the_same_instant_are_ordered_by_identity()
    {
        var keys = Keys(
            (SftpTestKeys.P384, "a", 0),
            (SftpTestKeys.Ed25519, "b", 0),
            (SftpTestKeys.P256, "c", 0));

        var forward = SftpAuthorizedKeysMapper.From(keys);
        var backward = SftpAuthorizedKeysMapper.From(keys.AsEnumerable().Reverse());

        Assert.Equal(forward, backward);
        Assert.Equal(keys.OrderBy(key =>
        {
            return key.Id;
        }).Select(key =>
{
    return key.PublicKey;
}), forward);
    }

    /// <summary>A login with no keys yields an empty set, which tells the agent to clear the file.</summary>
    [Fact]
    public void A_login_with_no_keys_yields_an_empty_set()
    {
        Assert.Empty(SftpAuthorizedKeysMapper.From([]));
    }

    /// <summary>A missing collection is refused rather than treated as an empty set.</summary>
    [Fact]
    public void A_missing_collection_is_refused_rather_than_treated_as_an_empty_set()
    {
        // An empty set clears every key on the host; a null that quietly meant "empty" would revoke
        // access on a mistake.
        Assert.Throws<ArgumentNullException>(() =>
        {
            return SftpAuthorizedKeysMapper.From(null!);
        });
    }

    /// <summary>Builds key rows through the login, as production does.</summary>
    /// <param name="specs">Each key's line, label and registration offset in days.</param>
    private static List<SftpUserKey> Keys(params (string Line, string Label, int Day)[] specs)
    {
        var login = SftpTestContext.Row(Guid.NewGuid(), "alice", "deploy");

        return specs.Select(spec =>
        {
            return login.AddKey(
                Guid.NewGuid(), spec.Label, SftpPublicKey.Parse(spec.Line).Value, Start.AddDays(spec.Day)).Value;
        }).ToList();
    }
}
