using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.ValueObjects;

namespace Maran.Modules.Identity.Tests.Domain;

/// <summary>Behavioural contract of the audit row: sealing once, and the two kinds of erasure.</summary>
public sealed class AuditEventTests
{
    /// <summary>No names: the person's own subjects are not offered for replacement.</summary>
    private static readonly IReadOnlySet<string> NoNames = new HashSet<string>();

    /// <summary>The time the row was written.</summary>
    private static readonly DateTimeOffset Written = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    /// <summary>The time of the first erasure.</summary>
    private static readonly DateTimeOffset FirstErasure = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    /// <summary>The time of a later erasure.</summary>
    private static readonly DateTimeOffset LaterErasure = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    /// <summary>The login the tests erase.</summary>
    private static readonly Guid ErasedUser = new("aaaaaaaa-0000-4000-8000-000000000001");

    /// <summary>A live login that is not erased.</summary>
    private static readonly Guid OtherUser = new("aaaaaaaa-0000-4000-8000-000000000002");

    /// <summary>The account the row is about.</summary>
    private static readonly Guid Account = new("bbbbbbbb-0000-4000-8000-000000000001");

    /// <summary>The pseudonym id used by the erasures.</summary>
    private static readonly Guid Pseudonym = new("cccccccc-0000-4000-8000-000000000001");

    /// <summary>Builds an unsealed row for an actor, optionally viewed-as by an operator.</summary>
    /// <param name="actor">The actor's login id.</param>
    /// <param name="impersonator">The operator viewing as the actor, or null.</param>
    /// <param name="subjectUserId">The login the row is about, or null.</param>
    /// <returns>A fresh row carrying an address, a user agent, a subject and an account.</returns>
    private static AuditEvent NewRow(Guid actor, AuditImpersonator? impersonator = null, Guid? subjectUserId = null)
    {
        return new AuditEvent(
            Guid.NewGuid(),
            Written,
            actor,
            "alice",
            "SiteCreated",
            "alice@example.test",
            "203.0.113.7",
            "Mozilla/5.0",
            true,
            "corr-1",
            impersonator,
            Account,
            subjectUserId);
    }

    /// <summary>Builds a byte array of one repeated value.</summary>
    /// <param name="length">How many bytes.</param>
    /// <param name="value">The byte repeated.</param>
    /// <returns>The array.</returns>
    private static byte[] Bytes(int length, byte value)
    {
        return Enumerable.Repeat(value, length).ToArray();
    }

    /// <summary>Seals a row with well-formed values and a salt.</summary>
    /// <param name="row">The row to seal.</param>
    private static void SealWithSalt(AuditEvent row)
    {
        row.Seal(1, Bytes(AuditEvent.HashLength, 1), Bytes(AuditEvent.SaltLength, 2), Bytes(AuditEvent.HashLength, 3));
    }

    /// <summary>A new row is neither sealed nor erased and carries the account it was given.</summary>
    [Fact]
    public void A_new_row_is_unsealed_and_unerased_and_keeps_its_account()
    {
        var row = NewRow(ErasedUser);

        Assert.False(row.IsSealed);
        Assert.False(row.IsErased);
        Assert.Null(row.ChainIndex);
        Assert.Null(row.RowHash);
        Assert.Equal(Account, row.AccountId);
    }

    /// <summary>Sealing stores the index, the commitment, the salt and the hash.</summary>
    [Fact]
    public void Sealing_stores_the_proof()
    {
        var row = NewRow(ErasedUser);

        SealWithSalt(row);

        Assert.True(row.IsSealed);
        Assert.Equal(1L, row.ChainIndex);
        Assert.Equal(Bytes(AuditEvent.HashLength, 1), row.ErasableCommitment);
        Assert.Equal(Bytes(AuditEvent.SaltLength, 2), row.CommitmentSalt);
        Assert.Equal(Bytes(AuditEvent.HashLength, 3), row.RowHash);
    }

    /// <summary>A row cannot be sealed a second time.</summary>
    [Fact]
    public void Sealing_twice_throws()
    {
        var row = NewRow(ErasedUser);
        SealWithSalt(row);

        Assert.Throws<InvalidOperationException>(() =>
        {
            SealWithSalt(row);
        });
    }

    /// <summary>Sealing refuses a hash, a commitment or a salt of the wrong length, and a non-positive index.</summary>
    [Fact]
    public void Sealing_refuses_values_of_the_wrong_shape()
    {
        var row = NewRow(ErasedUser);

        Assert.Throws<ArgumentException>(() =>
        {
            row.Seal(1, Bytes(31, 1), Bytes(AuditEvent.SaltLength, 2), Bytes(AuditEvent.HashLength, 3));
        });
        Assert.Throws<ArgumentException>(() =>
        {
            row.Seal(1, Bytes(AuditEvent.HashLength, 1), Bytes(15, 2), Bytes(AuditEvent.HashLength, 3));
        });
        Assert.Throws<ArgumentException>(() =>
        {
            row.Seal(1, Bytes(AuditEvent.HashLength, 1), Bytes(AuditEvent.SaltLength, 2), Bytes(33, 3));
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            row.Seal(0, Bytes(AuditEvent.HashLength, 1), Bytes(AuditEvent.SaltLength, 2), Bytes(AuditEvent.HashLength, 3));
        });
        Assert.False(row.IsSealed);
    }

    /// <summary>A row that is not erased must be sealed with a salt.</summary>
    [Fact]
    public void An_unerased_row_cannot_be_sealed_without_a_salt()
    {
        var row = NewRow(ErasedUser);

        Assert.Throws<ArgumentException>(() =>
        {
            row.Seal(1, Bytes(AuditEvent.HashLength, 1), null, Bytes(AuditEvent.HashLength, 3));
        });
    }

    /// <summary>A row erased before it was sealed is sealed without a salt, and cannot be given one.</summary>
    [Fact]
    public void An_erased_row_is_sealed_without_a_salt()
    {
        var withSalt = NewRow(ErasedUser);
        withSalt.EraseAccount(FirstErasure, Pseudonym, "erased-account-1");
        var withoutSalt = NewRow(ErasedUser);
        withoutSalt.EraseAccount(FirstErasure, Pseudonym, "erased-account-1");

        Assert.Throws<ArgumentException>(() =>
        {
            SealWithSalt(withSalt);
        });
        withoutSalt.Seal(7, Bytes(AuditEvent.HashLength, 1), null, Bytes(AuditEvent.HashLength, 3));

        Assert.True(withoutSalt.IsSealed);
        Assert.Null(withoutSalt.CommitmentSalt);
    }

    /// <summary>Erasing a person replaces the actor, the address and the user agent, and keeps what happened.</summary>
    [Fact]
    public void Erasing_the_actor_replaces_exactly_the_person_columns()
    {
        var row = NewRow(ErasedUser);
        var id = row.Id;

        row.ErasePerson(ErasedUser, FirstErasure, Pseudonym, "erased-user-1", NoNames);

        Assert.Equal(Pseudonym, row.ActorUserId);
        Assert.Equal("erased-user-1", row.ActorUsername);
        Assert.Equal(string.Empty, row.IpAddress);
        Assert.Equal(string.Empty, row.UserAgent);
        Assert.Equal("alice@example.test", row.Subject);
        Assert.Equal(id, row.Id);
        Assert.Equal(Written, row.OccurredAt);
        Assert.Equal("SiteCreated", row.Action);
        Assert.True(row.Succeeded);
        Assert.Equal("corr-1", row.CorrelationId);
        Assert.Equal(Account, row.AccountId);
        Assert.Equal(FirstErasure, row.ErasedAt);
        Assert.True(row.IsErased);
    }

    /// <summary>The subject text is replaced on a row the person acted in only when it is one of their own names.</summary>
    [Fact]
    public void The_subject_of_the_persons_own_row_is_replaced_only_when_it_is_their_name()
    {
        var kept = NewRow(ErasedUser);
        var replaced = NewRow(ErasedUser);

        kept.ErasePerson(ErasedUser, FirstErasure, Pseudonym, "erased-user-1", NoNames);
        replaced.ErasePerson(
            ErasedUser,
            FirstErasure,
            Pseudonym,
            "erased-user-1",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ALICE@example.test" });

        Assert.Equal("alice@example.test", kept.Subject);
        Assert.Equal("erased-user-1", replaced.Subject);
    }

    /// <summary>A row done to the person by somebody else loses the subject login and text and keeps the living actor's columns.</summary>
    [Fact]
    public void A_row_about_the_person_as_subject_login_loses_the_subject_and_keeps_the_actor()
    {
        var row = NewRow(OtherUser, subjectUserId: ErasedUser);

        row.ErasePerson(ErasedUser, FirstErasure, Pseudonym, "erased-user-1", NoNames);

        Assert.Equal("erased-user-1", row.Subject);
        Assert.Equal(Pseudonym, row.SubjectUserId);
        Assert.Equal(OtherUser, row.ActorUserId);
        Assert.Equal("alice", row.ActorUsername);
        Assert.Equal("203.0.113.7", row.IpAddress);
        Assert.Equal("Mozilla/5.0", row.UserAgent);
        Assert.True(row.IsErased);
    }

    /// <summary>An erased operator who viewed as somebody else is replaced as impersonator while the actor is kept.</summary>
    [Fact]
    public void An_erased_impersonator_is_replaced_and_the_living_actor_is_kept()
    {
        var row = NewRow(OtherUser, new AuditImpersonator(ErasedUser, "operator"));

        row.ErasePerson(ErasedUser, FirstErasure, Pseudonym, "erased-user-1", NoNames);

        Assert.Equal(OtherUser, row.ActorUserId);
        Assert.Equal("alice", row.ActorUsername);
        Assert.NotNull(row.Impersonator);
        Assert.Equal(Pseudonym, row.Impersonator.OperatorId);
        Assert.Equal("erased-user-1", row.Impersonator.Username);
        Assert.Equal(string.Empty, row.IpAddress);
        Assert.Equal(string.Empty, row.UserAgent);
    }

    /// <summary>
    /// A refused sign-in that merely typed the person's name carries no identity and is never touched, even
    /// when the caller offers that very name as one of the person's own (issue #464).
    /// </summary>
    [Fact]
    public void An_unidentified_claim_of_the_persons_name_is_left_alone()
    {
        var claim = new AuditEvent(
            Guid.NewGuid(),
            Written,
            null,
            "alice@example.test",
            "LoginFailed",
            "alice@example.test",
            "203.0.113.7",
            "Mozilla/5.0",
            false,
            null);

        claim.ErasePerson(
            ErasedUser,
            FirstErasure,
            Pseudonym,
            "erased-user-1",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alice@example.test" });

        Assert.Equal("alice@example.test", claim.ActorUsername);
        Assert.Equal("alice@example.test", claim.Subject);
        Assert.Equal("203.0.113.7", claim.IpAddress);
        Assert.Equal("Mozilla/5.0", claim.UserAgent);
        Assert.False(claim.IsErased);
    }

    /// <summary>A row that is about somebody else entirely is left untouched, erasure mark included.</summary>
    [Fact]
    public void A_row_that_does_not_match_the_person_is_left_alone()
    {
        var row = NewRow(OtherUser, new AuditImpersonator(OtherUser, "operator"));

        row.ErasePerson(ErasedUser, FirstErasure, Pseudonym, "erased-user-1", NoNames);

        Assert.Equal(OtherUser, row.ActorUserId);
        Assert.Equal("203.0.113.7", row.IpAddress);
        Assert.Equal("Mozilla/5.0", row.UserAgent);
        Assert.False(row.IsErased);
    }

    /// <summary>Erasing an account replaces its id and the subject and keeps the actor columns.</summary>
    [Fact]
    public void Erasing_the_account_replaces_exactly_the_account_columns()
    {
        var row = NewRow(OtherUser);

        row.EraseAccount(FirstErasure, Pseudonym, "erased-account-1");

        Assert.Equal(Pseudonym, row.AccountId);
        Assert.Equal("erased-account-1", row.Subject);
        Assert.Equal(OtherUser, row.ActorUserId);
        Assert.Equal("alice", row.ActorUsername);
        Assert.Equal("203.0.113.7", row.IpAddress);
        Assert.Equal("Mozilla/5.0", row.UserAgent);
        Assert.Equal("SiteCreated", row.Action);
        Assert.Equal(FirstErasure, row.ErasedAt);
    }

    /// <summary>Erasure destroys the salt of a sealed row and keeps the commitment, the hash and the index.</summary>
    [Fact]
    public void Erasure_destroys_the_salt_and_keeps_the_rest_of_the_proof()
    {
        var person = NewRow(ErasedUser);
        var account = NewRow(ErasedUser);
        SealWithSalt(person);
        SealWithSalt(account);

        person.ErasePerson(ErasedUser, FirstErasure, Pseudonym, "erased-user-1", NoNames);
        account.EraseAccount(FirstErasure, Pseudonym, "erased-account-1");

        foreach (var row in new[] { person, account })
        {
            Assert.Null(row.CommitmentSalt);
            Assert.Equal(Bytes(AuditEvent.HashLength, 1), row.ErasableCommitment);
            Assert.Equal(Bytes(AuditEvent.HashLength, 3), row.RowHash);
            Assert.Equal(1L, row.ChainIndex);
        }
    }

    /// <summary>The time of the first erasure survives a later one.</summary>
    [Fact]
    public void The_first_erasure_time_is_kept()
    {
        var row = NewRow(ErasedUser);

        row.EraseAccount(FirstErasure, Pseudonym, "erased-account-1");
        row.ErasePerson(ErasedUser, LaterErasure, Pseudonym, "erased-user-1", NoNames);

        Assert.Equal(FirstErasure, row.ErasedAt);
    }

    /// <summary>A pseudonym must say something.</summary>
    [Fact]
    public void A_blank_pseudonym_is_refused()
    {
        var row = NewRow(ErasedUser);

        Assert.Throws<ArgumentException>(() =>
        {
            row.ErasePerson(ErasedUser, FirstErasure, Pseudonym, " ", NoNames);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            row.EraseAccount(FirstErasure, Pseudonym, string.Empty);
        });
        Assert.False(row.IsErased);
    }
}
