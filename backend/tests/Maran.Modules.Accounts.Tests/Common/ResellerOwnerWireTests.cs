using System.Text.Json;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Enums;

namespace Maran.Modules.Accounts.Tests.Common;

/// <summary>
/// Behavioral contract of the owner members on the account wire shapes: a reseller's account writes its
/// owner as <c>resellerId</c> and <c>resellerName</c>, and an administrator's writes both as null, never
/// as an empty string or a nil id, so a screen can tell "the administrator's own" from an owner.
/// </summary>
public sealed class ResellerOwnerWireTests
{
    /// <summary>The options the panel's API writes with, for the parts that matter here.</summary>
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    /// <summary>A reseller's account writes its owner id and name.</summary>
    [Fact]
    public void A_resellers_account_writes_its_owner_id_and_name()
    {
        var owner = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var dto = new AccountDto(
            Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), AccountStatus.Active, "Live",
            DateTimeOffset.UnixEpoch, owner, "Acme Hosting");

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, Wire));

        Assert.Equal(owner.ToString(), json.RootElement.GetProperty("resellerId").GetString());
        Assert.Equal("Acme Hosting", json.RootElement.GetProperty("resellerName").GetString());
    }

    /// <summary>An administrator's account writes both owner members as null.</summary>
    [Fact]
    public void An_administrators_account_writes_both_owner_members_as_null()
    {
        var dto = new AccountDetailDto(
            Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), AccountStatus.Active, "Live",
            DateTimeOffset.UnixEpoch, "Agency", "owner@example.com", null, null);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, Wire));

        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("resellerId").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("resellerName").ValueKind);
    }

    /// <summary>The facts of a plan over its envelope write the offending figures under <c>fields</c>.</summary>
    [Fact]
    public void The_facts_of_a_plan_over_its_envelope_write_the_offending_figures_under_fields()
    {
        var json = JsonSerializer.Serialize(new PlanEnvelopeViolationsDto(["diskQuotaMb", "features"]), Wire);

        Assert.Equal("""{"fields":["diskQuotaMb","features"]}""", json);
    }
}
