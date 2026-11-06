using Maran.Host.IntegrationTests.Fixtures;

namespace Maran.Host.IntegrationTests.ContractSnapshots;

/// <summary>
/// The golden comparer itself, proven against deliberate input (rules/testing.md): a shape change must
/// be caught, and a change in what merely varies per run must not be. Without the positive controls the
/// snapshot tests could pass because the comparer sees nothing, which is the failure they exist to catch.
/// </summary>
public sealed class ContractGoldenComparerTests
{
    /// <summary>The shape of a creation answer, as the server writes it.</summary>
    private const string Account =
        """{"id":"3f2c1c9e-7f4e-4f3a-9d8e-0a1b2c3d4e5f","name":"acme","createdAt":"2026-11-05T10:00:00+00:00","status":"active"}""";

    /// <summary>The same shape with other ids, another time and another property order.</summary>
    private const string AccountSameShape =
        """{"status":"active","createdAt":"2027-01-01T00:00:00.1234567Z","name":"acme","id":"9a8b7c6d-1111-2222-3333-444455556666"}""";

    /// <summary>Two documents that differ only in ids, timestamps and property order are the same contract.</summary>
    [Fact]
    public void Documents_that_differ_only_in_ids_timestamps_and_property_order_have_no_difference()
    {
        var difference = ContractGolden.Diff(
            JsonShapeNormaliser.Normalise(Account), JsonShapeNormaliser.Normalise(AccountSameShape));

        Assert.Null(difference);
    }

    /// <summary>The normalised form is sorted, indented and carries the placeholders the goldens are written with.</summary>
    [Fact]
    public void Normalising_sorts_properties_and_replaces_ids_and_timestamps_with_placeholders()
    {
        var normalised = JsonShapeNormaliser.Normalise(Account);

        Assert.Equal(
            "{\n  \"createdAt\": \"<timestamp>\",\n  \"id\": \"<guid>\",\n  \"name\": \"acme\",\n  \"status\": \"active\"\n}",
            normalised);
    }

    /// <summary>A property added to the response is a difference, reported as a <c>+</c> line.</summary>
    [Fact]
    public void A_property_added_to_the_response_is_reported()
    {
        var live = Account.Replace("\"status\":\"active\"", "\"status\":\"active\",\"resellerId\":null", StringComparison.Ordinal);

        var difference = ContractGolden.Diff(
            JsonShapeNormaliser.Normalise(Account), JsonShapeNormaliser.Normalise(live));

        Assert.NotNull(difference);
        Assert.Contains("+   \"resellerId\": null", difference, StringComparison.Ordinal);
    }

    /// <summary>A property removed from the response is a difference, reported as a <c>-</c> line.</summary>
    [Fact]
    public void A_property_removed_from_the_response_is_reported()
    {
        var live = Account.Replace(",\"status\":\"active\"", string.Empty, StringComparison.Ordinal);

        var difference = ContractGolden.Diff(
            JsonShapeNormaliser.Normalise(Account), JsonShapeNormaliser.Normalise(live));

        Assert.NotNull(difference);
        Assert.Contains("-   \"status\": \"active\"", difference, StringComparison.Ordinal);
    }

    /// <summary>A property that changes kind, a string becoming a number, is a difference.</summary>
    [Fact]
    public void A_property_that_changes_its_value_kind_is_reported()
    {
        var live = Account.Replace("\"name\":\"acme\"", "\"name\":7", StringComparison.Ordinal);

        var difference = ContractGolden.Diff(
            JsonShapeNormaliser.Normalise(Account), JsonShapeNormaliser.Normalise(live));

        Assert.NotNull(difference);
        Assert.Contains("-   \"name\": \"acme\"", difference, StringComparison.Ordinal);
        Assert.Contains("+   \"name\": 7", difference, StringComparison.Ordinal);
    }

    /// <summary>A problem document's per-request correlation id and wording never make a difference.</summary>
    [Fact]
    public void A_problems_correlation_id_and_wording_are_normalised_away()
    {
        const string first = """{"code":"AccountNotFound","status":404,"title":"Not found","detail":"Not found","correlationId":"abc"}""";
        const string second = """{"code":"AccountNotFound","status":404,"title":"Nie znaleziono","detail":"Nie znaleziono","correlationId":"def"}""";

        Assert.Null(ContractGolden.Diff(JsonShapeNormaliser.Normalise(first), JsonShapeNormaliser.Normalise(second)));
    }

    /// <summary>
    /// A golden that is missing is a failure that says how to create it, never a silent pass; the live
    /// response is included so the person creating it can read what they are about to commit.
    /// </summary>
    [Fact]
    public void A_missing_golden_is_a_failure_that_names_how_to_create_it()
    {
        var failure = ContractGolden.Compare("no-such-golden-exists", Account);

        Assert.NotNull(failure);
        Assert.Contains(ContractGolden.UpdateVariable, failure, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"acme\"", failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// The committed creation golden accepts a live creation of the same shape and refuses a live
    /// creation with an extra field: the comparer, the normaliser and the committed file working together.
    /// </summary>
    [Fact]
    public void The_committed_creation_golden_accepts_its_own_shape_and_refuses_a_changed_one()
    {
        const string live =
            """{"id":"3f2c1c9e-7f4e-4f3a-9d8e-0a1b2c3d4e5f","name":"acme","primaryDomain":"acme.example.com","planId":"9a8b7c6d-1111-2222-3333-444455556666","status":"active","statusName":"Active","createdAt":"2026-11-05T10:00:00+00:00","resellerId":null,"resellerName":null}""";
        var changed = live.Replace("\"resellerName\":null", "\"resellerName\":null,\"extra\":1", StringComparison.Ordinal);

        Assert.Null(ContractGolden.Compare("accounts-create-201", live));
        var failure = ContractGolden.Compare("accounts-create-201", changed);

        Assert.NotNull(failure);
        Assert.Contains("+   \"extra\": 1", failure, StringComparison.Ordinal);
    }
}
