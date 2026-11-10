using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;

namespace Maran.Modules.Dns.Tests.Domain.Policies;

/// <summary>
/// Who may change or remove a record, by who wrote it: the customer's own and a template's are the owner's,
/// everything another facility keeps is not.
/// </summary>
public sealed class RecordOriginPolicyTests
{
    /// <summary>The decided answer for every origin; an origin missing from here fails the census below.</summary>
    private static readonly Dictionary<RecordOrigin, bool> Decided = new()
    {
        [RecordOrigin.Customer] = true,
        [RecordOrigin.Template] = true,
        [RecordOrigin.System] = false,
        [RecordOrigin.Mail] = false,
        [RecordOrigin.AcmeChallenge] = false,
    };

    /// <summary>The owner may edit a customer record and a template record.</summary>
    [Theory]
    [InlineData(RecordOrigin.Customer)]
    [InlineData(RecordOrigin.Template)]
    public void The_owner_may_edit_customer_and_template_records(RecordOrigin origin)
    {
        Assert.True(RecordOriginPolicy.IsOwnerEditable(origin));
    }

    /// <summary>The owner may not edit what the panel, the mail facility or a certificate validation keeps.</summary>
    [Theory]
    [InlineData(RecordOrigin.System)]
    [InlineData(RecordOrigin.Mail)]
    [InlineData(RecordOrigin.AcmeChallenge)]
    public void The_owner_may_not_edit_what_another_facility_keeps(RecordOrigin origin)
    {
        Assert.False(RecordOriginPolicy.IsOwnerEditable(origin));
    }

    /// <summary>Every origin has a decided answer, so a new origin cannot arrive editable by default.</summary>
    [Fact]
    public void Every_origin_has_a_decided_answer()
    {
        Assert.Equal(
            Enum.GetValues<RecordOrigin>().Order(),
            Decided.Keys.Order());
        foreach (var pair in Decided)
        {
            Assert.Equal(pair.Value, RecordOriginPolicy.IsOwnerEditable(pair.Key));
        }
    }

    /// <summary>Editing a template record, or a customer one, leaves a customer record.</summary>
    [Theory]
    [InlineData(RecordOrigin.Template)]
    [InlineData(RecordOrigin.Customer)]
    public void An_owner_edit_leaves_a_customer_record(RecordOrigin origin)
    {
        Assert.Equal(RecordOrigin.Customer, RecordOriginPolicy.AfterOwnerEdit(origin));
    }

    /// <summary>An edit of a record the owner may not edit cannot be described, and says so.</summary>
    [Fact]
    public void An_edit_of_a_system_record_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            return RecordOriginPolicy.AfterOwnerEdit(RecordOrigin.System);
        });
    }
}
