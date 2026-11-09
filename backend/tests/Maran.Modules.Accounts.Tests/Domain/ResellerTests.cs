using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Domain;

/// <summary>The rules <see cref="Reseller"/> holds itself to: creation, envelope, lifecycle and claim.</summary>
public sealed class ResellerTests
{
    /// <summary>A new reseller starts active and carries what it was created with.</summary>
    [Fact]
    public void A_new_reseller_starts_active_and_carries_what_it_was_created_with()
    {
        var id = Guid.NewGuid();
        var envelope = ResellerTestData.Envelope(maxAccounts: 3);

        var reseller = new Reseller(id, "acme", "owner@example.com", "ru", envelope, ResellerTestData.CreatedAt);

        Assert.Equal(id, reseller.Id);
        Assert.Equal("acme", reseller.Name);
        Assert.Equal("owner@example.com", reseller.ContactEmail);
        Assert.Equal("ru", reseller.Language);
        Assert.Equal(ResellerStatus.Active, reseller.Status);
        Assert.Equal(envelope, reseller.Envelope);
        Assert.Equal(ResellerTestData.CreatedAt, reseller.CreatedAt);
        Assert.Equal(0, reseller.AllocationRevision);
    }

    /// <summary>An empty or white-space text argument is refused at creation.</summary>
    /// <param name="name">The name.</param>
    /// <param name="email">The contact address.</param>
    /// <param name="language">The language.</param>
    [Theory]
    [InlineData("", "a@example.com", "en")]
    [InlineData(" ", "a@example.com", "en")]
    [InlineData("acme", "", "en")]
    [InlineData("acme", "   ", "en")]
    [InlineData("acme", "a@example.com", "")]
    [InlineData("acme", "a@example.com", " ")]
    public void A_blank_text_argument_is_refused_at_creation(string name, string email, string language)
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return new Reseller(
                Guid.NewGuid(),
                name,
                email,
                language,
                ResellerTestData.Envelope(),
                ResellerTestData.CreatedAt);
        });
    }

    /// <summary>A null text argument is refused at creation.</summary>
    [Fact]
    public void A_null_name_is_refused_at_creation()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            return new Reseller(
                Guid.NewGuid(),
                null!,
                "a@example.com",
                "en",
                ResellerTestData.Envelope(),
                ResellerTestData.CreatedAt);
        });
    }

    /// <summary>A null envelope is refused at creation.</summary>
    [Fact]
    public void A_null_envelope_is_refused_at_creation()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            return new Reseller(Guid.NewGuid(), "acme", "a@example.com", "en", null!, ResellerTestData.CreatedAt);
        });
    }

    /// <summary>Changing the envelope replaces it.</summary>
    [Fact]
    public void Changing_the_envelope_replaces_it()
    {
        var reseller = ResellerTestData.Reseller();
        var replacement = ResellerTestData.Envelope(maxAccounts: 1, diskPoolMb: 5);

        reseller.ChangeEnvelope(replacement);

        Assert.Equal(replacement, reseller.Envelope);
    }

    /// <summary>A null replacement envelope is refused and the old one is kept.</summary>
    [Fact]
    public void A_null_replacement_envelope_is_refused_and_the_old_one_is_kept()
    {
        var reseller = ResellerTestData.Reseller();
        var before = reseller.Envelope;

        Assert.Throws<ArgumentNullException>(() =>
        {
            reseller.ChangeEnvelope(null!);
        });

        Assert.Equal(before, reseller.Envelope);
    }

    /// <summary>Suspending marks the reseller suspended, and doing it twice changes nothing more.</summary>
    [Fact]
    public void Suspending_marks_the_reseller_suspended_and_is_idempotent()
    {
        var reseller = ResellerTestData.Reseller();

        reseller.Suspend();
        reseller.Suspend();

        Assert.Equal(ResellerStatus.Suspended, reseller.Status);
    }

    /// <summary>Reactivating a suspended reseller makes it active, and doing it twice changes nothing more.</summary>
    [Fact]
    public void Reactivating_marks_the_reseller_active_and_is_idempotent()
    {
        var reseller = ResellerTestData.Reseller();
        reseller.Suspend();

        reseller.Reactivate();
        reseller.Reactivate();

        Assert.Equal(ResellerStatus.Active, reseller.Status);
    }

    /// <summary>Reactivating a reseller that was never suspended leaves it active.</summary>
    [Fact]
    public void Reactivating_an_active_reseller_leaves_it_active()
    {
        var reseller = ResellerTestData.Reseller();

        reseller.Reactivate();

        Assert.Equal(ResellerStatus.Active, reseller.Status);
    }

    /// <summary>Each noted allocation change moves the concurrency revision by one.</summary>
    [Fact]
    public void Noting_an_allocation_change_increments_the_revision_each_time()
    {
        var reseller = ResellerTestData.Reseller();

        reseller.NoteAllocationChanged();
        reseller.NoteAllocationChanged();

        Assert.Equal(2, reseller.AllocationRevision);
    }

    /// <summary>Suspending and changing the envelope never move the allocation revision.</summary>
    [Fact]
    public void Suspending_and_changing_the_envelope_do_not_touch_the_revision()
    {
        var reseller = ResellerTestData.Reseller();

        reseller.Suspend();
        reseller.ChangeEnvelope(ResellerTestData.Envelope(maxAccounts: 2));

        Assert.Equal(0, reseller.AllocationRevision);
    }

    /// <summary>Renaming replaces the name and nothing else.</summary>
    [Fact]
    public void Renaming_replaces_the_name_and_nothing_else()
    {
        var reseller = ResellerTestData.Reseller("acme");

        reseller.Rename("globex");

        Assert.Equal("globex", reseller.Name);
        Assert.Equal("acme@example.com", reseller.ContactEmail);
        Assert.Equal("en", reseller.Language);
    }

    /// <summary>A blank new name is refused and the old one is kept.</summary>
    /// <param name="name">The blank name.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_new_name_is_refused_and_the_old_one_is_kept(string name)
    {
        var reseller = ResellerTestData.Reseller("acme");

        Assert.Throws<ArgumentException>(() =>
        {
            reseller.Rename(name);
        });

        Assert.Equal("acme", reseller.Name);
    }

    /// <summary>Updating the contact replaces the address and the language and leaves the name.</summary>
    [Fact]
    public void Updating_the_contact_replaces_the_address_and_the_language_and_leaves_the_name()
    {
        var reseller = ResellerTestData.Reseller("acme");

        reseller.UpdateContact("new@example.com", "hy");

        Assert.Equal("new@example.com", reseller.ContactEmail);
        Assert.Equal("hy", reseller.Language);
        Assert.Equal("acme", reseller.Name);
    }

    /// <summary>A blank contact address or language is refused and the old values are kept.</summary>
    /// <param name="email">The address.</param>
    /// <param name="language">The language.</param>
    [Theory]
    [InlineData("", "ru")]
    [InlineData("   ", "ru")]
    [InlineData("new@example.com", "")]
    [InlineData("new@example.com", " ")]
    public void A_blank_contact_value_is_refused_and_the_old_values_are_kept(string email, string language)
    {
        var reseller = ResellerTestData.Reseller("acme");

        Assert.Throws<ArgumentException>(() =>
        {
            reseller.UpdateContact(email, language);
        });

        Assert.Equal("acme@example.com", reseller.ContactEmail);
        Assert.Equal("en", reseller.Language);
    }
}
