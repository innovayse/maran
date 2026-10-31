using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The performance budget of issue #196: every paged list endpoint, over a dataset of more than a
/// thousand accounts, answers each shape the SPA sends — first page, deepest page, search, each
/// filter, each sort in both directions — within <see cref="BudgetMilliseconds"/> at the 95th
/// percentile of <see cref="MeasuredRuns"/> runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is timed.</b> The whole request through the in-process test server: authentication, model
/// binding, validation, the handler, every SQL statement against a real PostgreSQL, and the JSON
/// answer. Not the network, which a test server does not have, and not the agent, which the one list
/// that asks it (the account disk view) reaches through a stub — so that list's figure is the panel's
/// share of the cost only.
/// </para>
/// <para>
/// <b>Why 200 ms.</b> It is the line under which a list still reads as an immediate response to a
/// click rather than a wait. The slowest shape measured on a workstation over this dataset was 25 ms
/// on PostgreSQL 16 and 67 ms on 13 (docs/performance/list-indexes.md records every figure), so
/// the budget leaves three to eight times that for a slower CI runner and a cold cache — while a
/// query that loses its index, or a handler that starts paging in memory, costs far more than
/// that at this size and is caught.
/// </para>
/// <para>
/// <b>Why the 95th percentile.</b> One run can be held up by a garbage collection or the scheduler; a
/// budget on the maximum would make that noise a failure, which rules/testing.md ranks as a P1 bug.
/// The 95th percentile of thirty runs discards the single slowest one and nothing else, after
/// <see cref="WarmupRuns"/> unmeasured runs have compiled the query and warmed PostgreSQL's cache.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ListPerformanceBudgetTests : IClassFixture<ScaleDatasetFixture>
{
    /// <summary>The latency budget of one list request at the 95th percentile, in milliseconds.</summary>
    private const double BudgetMilliseconds = 200;

    /// <summary>Runs made before measuring, so the first query plan and a cold cache are not timed.</summary>
    private const int WarmupRuns = 3;

    /// <summary>Runs measured per shape; the 95th percentile of thirty discards only the slowest.</summary>
    private const int MeasuredRuns = 30;

    /// <summary>The panel booted over the scale dataset.</summary>
    private readonly ScaleDatasetFixture _dataset;

    /// <summary>Where each shape's figures are written, so a passing run still records them.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Binds the test to the seeded panel.</summary>
    /// <param name="dataset">The panel over the scale dataset, seeded once for the class.</param>
    /// <param name="output">The run's output for this case.</param>
    public ListPerformanceBudgetTests(ScaleDatasetFixture dataset, ITestOutputHelper output)
    {
        _dataset = dataset;
        _output = output;
    }

    /// <summary>The measured lists, one test case each.</summary>
    public static TheoryData<string> Lists
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var list in ScaleListShapes.Lists)
            {
                data.Add(list);
            }

            return data;
        }
    }

    /// <summary>
    /// Every shape of the list answers 200 within the budget at the 95th percentile over the scale
    /// dataset, and the run names each shape that did not.
    /// </summary>
    /// <param name="list">The list under measurement.</param>
    [Theory]
    [MemberData(nameof(Lists))]
    public async Task Every_shape_of_the_list_answers_within_its_budget_over_more_than_a_thousand_accounts(string list)
    {
        var client = _dataset.Administrator();
        var overBudget = new List<string>();

        foreach (var (name, path, minimumTotal) in ScaleListShapes.Of(list))
        {
            if (minimumTotal is { } seeded)
            {
                // The guard on the axis that goes blind: an empty table answers fast, so a seed that
                // wrote nothing — or a list that stopped seeing it — would pass every budget below.
                var total = await TotalOfAsync(client, path);
                Assert.True(total >= seeded, $"{list}: the first page reports {total} rows, but {seeded} were seeded.");
            }

            var p95 = await NinetyFifthPercentileAsync(client, path);
            _output.WriteLine($"{list} | {name} | p95 {p95:F1} ms");
            if (p95 > BudgetMilliseconds)
            {
                overBudget.Add($"{name} ({path}): p95 {p95:F1} ms");
            }
        }

        Assert.True(
            overBudget.Count == 0,
            $"{list}: shapes over the {BudgetMilliseconds} ms budget: " + string.Join("; ", overBudget));
    }

    /// <summary>Times one shape and returns its 95th-percentile latency.</summary>
    /// <param name="client">The administrator's client.</param>
    /// <param name="path">The shape's path and query string.</param>
    /// <returns>The 95th-percentile latency in milliseconds.</returns>
    private static async Task<double> NinetyFifthPercentileAsync(HttpClient client, string path)
    {
        for (var run = 0; run < WarmupRuns; run++)
        {
            await GetOkAsync(client, path);
        }

        var samples = new double[MeasuredRuns];
        for (var run = 0; run < MeasuredRuns; run++)
        {
            var started = Stopwatch.GetTimestamp();
            await GetOkAsync(client, path);
            samples[run] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        Array.Sort(samples);
        var rank = (int)Math.Ceiling(0.95 * MeasuredRuns) - 1;
        return samples[rank];
    }

    /// <summary>Reads the total row count a list page reports.</summary>
    /// <param name="client">The administrator's client.</param>
    /// <param name="path">The page's path and query string.</param>
    /// <returns>The page's <c>total</c>.</returns>
    private static async Task<int> TotalOfAsync(HttpClient client, string path)
    {
        using var body = JsonDocument.Parse(await GetOkAsync(client, path));
        return body.RootElement.GetProperty("total").GetInt32();
    }

    /// <summary>Reads the shape and requires a 200 with its whole body, so a refusal is never timed as a pass.</summary>
    /// <param name="client">The administrator's client.</param>
    /// <param name="path">The shape's path and query string.</param>
    /// <returns>The body.</returns>
    private static async Task<string> GetOkAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {(int)response.StatusCode}: {body}");
        return body;
    }
}
