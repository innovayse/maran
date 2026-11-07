using Maran.Modules.Identity.Domain.Policies;

namespace Maran.Modules.Identity.Tests.Domain.Policies;

/// <summary>Behavioural contract of the stand-ins an erasure writes into the audit journal.</summary>
public sealed class AuditPseudonymTests
{
    /// <summary>How many identifiers the randomness test draws.</summary>
    private const int Draws = 200;

    /// <summary>The name that stands in for a person is the prefix and the first eight hexadecimal digits of the id.</summary>
    [Fact]
    public void The_user_name_is_the_prefix_and_eight_hexadecimal_digits_of_the_id()
    {
        var id = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");

        Assert.Equal("erased-user-1a2b3c4d", AuditPseudonym.UserName(id));
    }

    /// <summary>The text that stands in for a deleted account is built the same way under its own prefix.</summary>
    [Fact]
    public void The_account_subject_is_the_prefix_and_eight_hexadecimal_digits_of_the_id()
    {
        var id = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");

        Assert.Equal("erased-account-1a2b3c4d", AuditPseudonym.AccountSubject(id));
    }

    /// <summary>The two prefixes differ, so a person's pseudonym is never mistaken for an account's.</summary>
    [Fact]
    public void The_two_prefixes_differ()
    {
        Assert.NotEqual(AuditPseudonym.UserNamePrefix, AuditPseudonym.AccountSubjectPrefix);
    }

    /// <summary>Identifiers are random and never the empty id, which the orphaned-actor sweep skips by design.</summary>
    [Fact]
    public void New_ids_are_random_and_never_empty()
    {
        var ids = Enumerable.Range(0, Draws).Select(_ => { return AuditPseudonym.NewId(); }).ToList();

        Assert.Equal(Draws, ids.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, ids);
    }

    /// <summary>Two different ids give two different names, apart from the stated eight-digit collision limit.</summary>
    [Fact]
    public void Ids_that_differ_in_their_first_digits_give_different_names()
    {
        var first = Guid.Parse("00000001-0000-4000-8000-000000000000");
        var second = Guid.Parse("00000002-0000-4000-8000-000000000000");

        Assert.NotEqual(AuditPseudonym.UserName(first), AuditPseudonym.UserName(second));
    }
}
