using Maran.Modules.Sftp.Domain.Entities;
using Maran.Modules.Sftp.Domain.Policies;
using Maran.Modules.Sftp.Domain.ValueObjects;
using Maran.Modules.Sftp.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sftp.Tests.Domain.Entities;

/// <summary>The rules a login enforces on its own set of public keys.</summary>
public sealed class SftpUserTests
{
    /// <summary>The instant keys are registered at in these tests.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Adding a valid key records it on the login with the label, algorithm and fingerprint.</summary>
    [Fact]
    public void Adding_a_valid_key_records_it_on_the_login()
    {
        var login = NewLogin();
        var id = Guid.NewGuid();

        var added = login.AddKey(id, "laptop", Parse(SftpTestKeys.Ed25519), Now);

        Assert.True(added.IsSuccess);
        var key = Assert.Single(login.Keys);
        Assert.Same(added.Value, key);
        Assert.Equal(id, key.Id);
        Assert.Equal(login.Id, key.SftpUserId);
        Assert.Equal("laptop", key.Label);
        Assert.Equal(SftpTestKeys.Ed25519, key.PublicKey);
        Assert.Equal("ssh-ed25519", key.Algorithm);
        Assert.Equal(SftpTestKeys.Ed25519Fingerprint, key.Fingerprint);
        Assert.Equal(Now, key.CreatedAt);
    }

    /// <summary>A label is trimmed before it is stored.</summary>
    [Fact]
    public void A_label_is_trimmed_before_it_is_stored()
    {
        var login = NewLogin();

        var added = login.AddKey(Guid.NewGuid(), "  work laptop \t", Parse(SftpTestKeys.Ed25519), Now);

        Assert.True(added.IsSuccess);
        Assert.Equal("work laptop", added.Value.Label);
    }

    /// <summary>A label of exactly the limit is accepted.</summary>
    [Fact]
    public void A_label_of_exactly_the_limit_is_accepted()
    {
        var login = NewLogin();

        var added = login.AddKey(
            Guid.NewGuid(), new string('l', SftpKeyLimits.MaxLabelLength), Parse(SftpTestKeys.Ed25519), Now);

        Assert.True(added.IsSuccess);
    }

    /// <summary>A label that is empty, blank, too long or carries a control character is refused and nothing is added.</summary>
    /// <param name="label">The label offered.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("line\nbreak")]
    [InlineData("tab\tinside")]
    [InlineData("nul\0inside")]
    [InlineData("esc\u001binside")]
    public void A_label_that_is_empty_blank_or_carries_a_control_character_is_refused(string label)
    {
        var login = NewLogin();

        var added = login.AddKey(Guid.NewGuid(), label, Parse(SftpTestKeys.Ed25519), Now);

        AssertFailure(added, "SftpKeyLabelInvalid", ErrorType.Validation);
        Assert.Empty(login.Keys);
    }

    /// <summary>A label one character over the limit is refused.</summary>
    [Fact]
    public void A_label_one_character_over_the_limit_is_refused()
    {
        var login = NewLogin();

        var added = login.AddKey(
            Guid.NewGuid(), new string('l', SftpKeyLimits.MaxLabelLength + 1), Parse(SftpTestKeys.Ed25519), Now);

        AssertFailure(added, "SftpKeyLabelInvalid", ErrorType.Validation);
        Assert.Empty(login.Keys);
    }

    /// <summary>A label whose length is only reached by surrounding spaces is measured after trimming.</summary>
    [Fact]
    public void A_label_is_measured_after_trimming()
    {
        var login = NewLogin();
        var padded = "  " + new string('l', SftpKeyLimits.MaxLabelLength) + "  ";

        Assert.True(login.AddKey(Guid.NewGuid(), padded, Parse(SftpTestKeys.Ed25519), Now).IsSuccess);
    }

    /// <summary>The same key twice on one login is a conflict and the second is not added.</summary>
    [Fact]
    public void The_same_key_twice_on_one_login_is_a_conflict()
    {
        var login = NewLogin();
        login.AddKey(Guid.NewGuid(), "first", Parse(SftpTestKeys.Ed25519), Now);

        // Same blob, different comment and label: the fingerprint is what identifies the key.
        var again = login.AddKey(
            Guid.NewGuid(), "second", Parse(SftpTestKeys.Ed25519 + " other-comment"), Now);

        AssertFailure(again, "SftpKeyDuplicate", ErrorType.Conflict);
        Assert.Single(login.Keys);
    }

    /// <summary>The same key on two different logins is allowed: a fingerprint is per login.</summary>
    [Fact]
    public void The_same_key_on_two_different_logins_is_allowed()
    {
        var first = NewLogin();
        var second = NewLogin();

        Assert.True(first.AddKey(Guid.NewGuid(), "a", Parse(SftpTestKeys.Ed25519), Now).IsSuccess);
        Assert.True(second.AddKey(Guid.NewGuid(), "a", Parse(SftpTestKeys.Ed25519), Now).IsSuccess);
    }

    /// <summary>A login may hold exactly the maximum number of keys.</summary>
    [Fact]
    public void A_login_may_hold_exactly_the_maximum_number_of_keys()
    {
        var login = NewLogin();

        for (var index = 0; index < SftpKeyLimits.MaxPerLogin; index++)
        {
            Assert.True(login.AddKey(Guid.NewGuid(), $"key {index}", Parse(SftpTestKeys.Distinct(index)), Now).IsSuccess);
        }

        Assert.Equal(SftpKeyLimits.MaxPerLogin, login.Keys.Count);
    }

    /// <summary>The key after the maximum is a conflict and is not added.</summary>
    [Fact]
    public void The_key_after_the_maximum_is_a_conflict()
    {
        var login = FullLogin();

        var over = login.AddKey(
            Guid.NewGuid(), "one too many", Parse(SftpTestKeys.Distinct(SftpKeyLimits.MaxPerLogin)), Now);

        AssertFailure(over, "SftpKeyLimitReached", ErrorType.Conflict);
        Assert.Equal(SftpKeyLimits.MaxPerLogin, login.Keys.Count);
    }

    /// <summary>On a full login a key it already holds is reported as a duplicate, not as the limit.</summary>
    [Fact]
    public void On_a_full_login_a_key_it_already_holds_is_reported_as_a_duplicate()
    {
        var login = FullLogin();

        var again = login.AddKey(Guid.NewGuid(), "again", Parse(SftpTestKeys.Distinct(0)), Now);

        AssertFailure(again, "SftpKeyDuplicate", ErrorType.Conflict);
    }

    /// <summary>A bad label is reported before a duplicate key.</summary>
    [Fact]
    public void A_bad_label_is_reported_before_a_duplicate_key()
    {
        var login = NewLogin();
        login.AddKey(Guid.NewGuid(), "first", Parse(SftpTestKeys.Ed25519), Now);

        var refused = login.AddKey(Guid.NewGuid(), " ", Parse(SftpTestKeys.Ed25519), Now);

        AssertFailure(refused, "SftpKeyLabelInvalid", ErrorType.Validation);
    }

    /// <summary>Removing a key takes it out of the login and hands it back for the journal.</summary>
    [Fact]
    public void Removing_a_key_takes_it_out_of_the_login_and_hands_it_back()
    {
        var login = NewLogin();
        var keepId = Guid.NewGuid();
        var dropId = Guid.NewGuid();
        login.AddKey(keepId, "keep", Parse(SftpTestKeys.Ed25519), Now);
        login.AddKey(dropId, "drop", Parse(SftpTestKeys.P256), Now);

        var removed = login.RemoveKey(dropId);

        Assert.True(removed.IsSuccess);
        Assert.Equal("drop", removed.Value.Label);
        Assert.Equal(SftpTestKeys.P256Fingerprint, removed.Value.Fingerprint);
        Assert.Equal([keepId], login.Keys.Select(key =>
        {
            return key.Id;
        }));
    }

    /// <summary>Removing a key the login does not hold is not found and changes nothing.</summary>
    [Fact]
    public void Removing_a_key_the_login_does_not_hold_is_not_found()
    {
        var login = NewLogin();
        login.AddKey(Guid.NewGuid(), "only", Parse(SftpTestKeys.Ed25519), Now);

        var removed = login.RemoveKey(Guid.NewGuid());

        AssertFailure(removed, "SftpKeyNotFound", ErrorType.NotFound);
        Assert.Single(login.Keys);
    }

    /// <summary>A key of another login is not found on this one, even though it exists.</summary>
    [Fact]
    public void A_key_of_another_login_is_not_found_on_this_one()
    {
        var mine = NewLogin();
        var theirs = NewLogin();
        var theirKey = theirs.AddKey(Guid.NewGuid(), "theirs", Parse(SftpTestKeys.Ed25519), Now).Value;

        var removed = mine.RemoveKey(theirKey.Id);

        AssertFailure(removed, "SftpKeyNotFound", ErrorType.NotFound);
        Assert.Single(theirs.Keys);
    }

    /// <summary>Removing a key frees the slot for a new one on a full login.</summary>
    [Fact]
    public void Removing_a_key_frees_the_slot_on_a_full_login()
    {
        var login = FullLogin();
        var first = login.Keys.First();

        Assert.True(login.RemoveKey(first.Id).IsSuccess);
        Assert.True(login.AddKey(
            Guid.NewGuid(), "replacement", Parse(SftpTestKeys.Distinct(SftpKeyLimits.MaxPerLogin)), Now).IsSuccess);
    }

    /// <summary>Builds a login with no keys.</summary>
    private static SftpUser NewLogin()
    {
        return SftpTestContext.Row(Guid.NewGuid(), "alice", "deploy");
    }

    /// <summary>Builds a login holding the maximum number of distinct keys.</summary>
    private static SftpUser FullLogin()
    {
        var login = NewLogin();
        for (var index = 0; index < SftpKeyLimits.MaxPerLogin; index++)
        {
            login.AddKey(Guid.NewGuid(), $"key {index}", Parse(SftpTestKeys.Distinct(index)), Now);
        }

        return login;
    }

    /// <summary>Parses a key the test knows is valid.</summary>
    /// <param name="line">The key line.</param>
    private static SftpPublicKey Parse(string line)
    {
        return SftpPublicKey.Parse(line).Value;
    }

    /// <summary>Asserts a failed result with the given code and kind.</summary>
    /// <typeparam name="T">The result's value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="code">The expected code.</param>
    /// <param name="type">The expected kind.</param>
    private static void AssertFailure<T>(Result<T> result, string code, ErrorType type)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(type, result.Error.Type);
    }
}
