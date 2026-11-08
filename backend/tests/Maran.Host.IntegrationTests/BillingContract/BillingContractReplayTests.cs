using System.Text.RegularExpressions;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Controllers.External;

namespace Maran.Host.IntegrationTests.BillingContract;

/// <summary>
/// The billing contract (<c>docs/api/billing-contract/lifecycle.json</c>) replayed against the real panel
/// over real HTTP and a real PostgreSQL (issue #7): the sequence a billing provider relies on to provision,
/// change and terminate a reseller's accounts, so a route, a status, a code, a required field or a type the
/// provider reads cannot change without this failing.
/// </summary>
/// <remarks>
/// <para>
/// <b>The replay must not be able to go blind.</b> It checks only what the file names, so a replayer that
/// ignored its expectations would pass every contract. Two things prevent that: a planted contract whose
/// every step differs from the panel in a different way must be reported, step by step, while one step that
/// matches is not (the positive and the inverse control, on the same replayer); and the real replay must
/// send every step of the file and skip none, so a missing input cannot quietly shrink what is checked.
/// </para>
/// <para>
/// The file is also held to the controllers: every route it uses is declared by a controller, and every
/// provisioning route a controller declares is in the file.
/// </para>
/// <para>
/// UNOBSERVED HERE: the agent. A double answers the host's side (a suspension, a measurement of disk, a
/// final backup), so what the real agent does to a machine is not part of this contract.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class BillingContractReplayTests : IAsyncLifetime
{
    /// <summary>Finds a route parameter, whatever it is called and however many braces the file spells it with.</summary>
    private static readonly Regex Parameter = new(@"\{+[^{}]+\}+", RegexOptions.Compiled);

    /// <summary>Finds a <c>{{name}}</c> placeholder.</summary>
    private static readonly Regex Placeholder = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public BillingContractReplayTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>Reads a route as "METHOD /path" with every parameter reduced to <c>{}</c>.</summary>
    private static string Shape(string route)
    {
        return Parameter.Replace(route, "{}");
    }

    /// <summary>The contract replayed against the panel finds no difference, sends every step and skips none.</summary>
    [Fact]
    public async Task The_billing_contract_replays_against_the_panel_without_a_difference()
    {
        var contract = BillingContractLoader.Load();
        await using var world = await BillingContractWorld.BootAsync(_pg);

        var report = await BillingContractReplayer.ReplayAsync(contract, world.ReplayEnvironment());

        Assert.True(report.Failures.Count == 0, string.Join("\n", report.Failures));
        Assert.True(report.Skipped.Count == 0, "Skipped, which is never a pass: " + string.Join("; ", report.Skipped));
        Assert.Equal(contract.Interactions.Count, report.Replayed);
    }

    /// <summary>
    /// A contract that differs from the panel in six ways is reported in all six, naming each step, and the step
    /// that matches is not reported: the replay can fail, and it can pass.
    /// </summary>
    [Fact]
    public async Task A_planted_difference_between_the_contract_and_the_panel_is_reported_and_a_match_is_not()
    {
        await using var world = await BillingContractWorld.BootAsync(_pg);
        var info = new BillingRequest("GET", "/info");
        var planted = new BillingContractFile(
            "planted",
            1,
            BillingContractWorld.ProvisioningRoute,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            [
                new BillingInteraction(
                    "matches",
                    info,
                    new BillingResponse(200, Properties: [new ResponseExpectation("apiRevision", "integer")])),
                new BillingInteraction("wrong-status", info, new BillingResponse(201)),
                new BillingInteraction(
                    "missing-property",
                    info,
                    new BillingResponse(200, Properties: [new ResponseExpectation("noSuchField", "string")])),
                new BillingInteraction(
                    "retyped-property",
                    info,
                    new BillingResponse(200, Properties: [new ResponseExpectation("apiRevision", "string")])),
                new BillingInteraction(
                    "wrong-value",
                    info,
                    new BillingResponse(
                        200, Properties: [new ResponseExpectation("apiRevision", "integer", EqualsLiteral: "-1")])),
                new BillingInteraction("property-that-must-be-absent", info, new BillingResponse(200, Absent: ["apiRevision"])),
                new BillingInteraction(
                    "wrong-code",
                    new BillingRequest("GET", "/accounts/{{randomGuid}}"),
                    new BillingResponse(404, Code: "SomethingElse")),
                new BillingInteraction("needs-an-arrangement", info, new BillingResponse(200), Arrange: "notOffered"),
            ]);

        var report = await BillingContractReplayer.ReplayAsync(planted, world.ReplayEnvironment());

        Assert.Equal(7, report.Replayed);
        Assert.Equal(6, report.Failures.Count);
        foreach (var step in new[]
        {
            "wrong-status", "missing-property", "retyped-property", "wrong-value", "property-that-must-be-absent", "wrong-code",
        })
        {
            Assert.Single(report.Failures, failure => { return failure.StartsWith(step + ":", StringComparison.Ordinal); });
        }

        Assert.DoesNotContain(report.Failures, failure => { return failure.StartsWith("matches:", StringComparison.Ordinal); });
        Assert.StartsWith("needs-an-arrangement:", Assert.Single(report.Skipped), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every route the contract sends is a route a provisioning controller declares, and every route those
    /// controllers declare is in the contract: a renamed route fails here, and so does a new one nobody wrote down.
    /// </summary>
    [Fact]
    public void The_contract_and_the_provisioning_controllers_declare_the_same_routes()
    {
        var contract = BillingContractLoader.Load();
        var declared = ControllerRoutes.Declared<ResellerKeyAccountWritesController>()
            .Concat(ControllerRoutes.Declared<ResellerKeyAccountTerminationController>())
            .Concat(ControllerRoutes.Declared<ResellerKeyAccountDetailsController>())
            .Select(Shape)
            .ToHashSet(StringComparer.Ordinal);
        var used = contract.Interactions
            .Select(interaction => { return Shape($"{interaction.Request.Method} {contract.BasePath}{interaction.Request.Path}"); })
            .ToHashSet(StringComparer.Ordinal);

        // The vacuity guard: the controllers' routes were read (eight of them), and a known one is among them.
        Assert.Equal(8, declared.Count);
        Assert.Contains("DELETE /api/v1/reseller-api/accounts/{}", declared);

        Assert.Empty(used.Except(declared));
        Assert.Empty(declared.Except(used));
    }

    /// <summary>
    /// The file is internally consistent and not hollow: it holds the kinds of answer a provider branches on,
    /// and every credential, input, arrangement and placeholder it uses is one it declares.
    /// </summary>
    [Fact]
    public void The_contract_is_complete_and_names_only_what_it_declares()
    {
        var contract = BillingContractLoader.Load();
        var statuses = contract.Interactions.Select(interaction => { return interaction.Response.Status; }).ToHashSet();

        Assert.True(contract.Interactions.Count >= 40, $"The contract has shrunk to {contract.Interactions.Count} steps.");
        foreach (var status in new[] { 200, 201, 400, 401, 403, 404, 409, 422, 500 })
        {
            Assert.Contains(status, statuses);
        }

        var captured = new HashSet<string>(StringComparer.Ordinal);
        var known = contract.Inputs.Keys.Concat(contract.Builtins.Keys).ToHashSet(StringComparer.Ordinal);
        var credentials = contract.Credentials.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var interaction in contract.Interactions)
        {
            Assert.Contains(interaction.Credential ?? "full", credentials);
            foreach (var input in interaction.Requires ?? [])
            {
                Assert.Contains(input, contract.Inputs.Keys);
            }

            if (interaction.Arrange is not null)
            {
                Assert.Contains(interaction.Arrange, contract.Arrangements.Keys);
            }

            var texts = new List<string> { interaction.Request.Path };
            texts.AddRange((interaction.Request.Headers ?? new Dictionary<string, string>()).Values);
            if (interaction.Request.Body is { } body)
            {
                texts.Add(body.GetRawText());
            }

            texts.AddRange((interaction.Response.Properties ?? []).Select(property => { return property.EqualsLiteral ?? string.Empty; }));
            foreach (var name in texts.SelectMany(text => { return Placeholder.Matches(text).Select(match => { return match.Groups[1].Value; }); }))
            {
                Assert.True(
                    known.Contains(name) || captured.Contains(name),
                    $"{interaction.Name}: '{{{{{name}}}}}' is neither a declared input or built-in nor captured by an earlier step.");
            }

            foreach (var variable in (interaction.Response.Properties ?? []).Select(property => { return property.EqualsVariable; }))
            {
                Assert.True(
                    variable is null || known.Contains(variable) || captured.Contains(variable),
                    $"{interaction.Name}: it compares with '{variable}', which no input, built-in or earlier step provides.");
            }

            captured.UnionWith((interaction.Response.Capture ?? new Dictionary<string, string>()).Keys);
        }
    }
}
