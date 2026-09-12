using Maran.Host.Resilience;

namespace Maran.Host.Tests.Resilience;

/// <summary>
/// The panel's decision NOT to serialise agent calls, pinned where it would be reversed: two
/// operations on one account are allowed to be inside the agent client at the same time.
/// </summary>
/// <remarks>
/// <para>
/// A test for an absence needs a reason to exist, and this one has it. The panel's contract with
/// the agent is that the AGENT is the authority on what may run at once: its per-resource locks are
/// held by the work itself, while any lock the panel took would be released the moment the panel
/// stopped watching — which, because a cancelled call detaches rather than cancels the agent's
/// blocking task, is not the moment the work stops. A panel-side gate would therefore be a second
/// authority that is wrong exactly when it matters, and the natural place for someone to add one is
/// the decorator layer this test drives.
/// </para>
/// <para>
/// So it is not decoration: with a per-account gate added to
/// <see cref="ResilientAgentFilesClient"/>, the second write never enters the inner client, the
/// first therefore never returns, and this test fails on its own deadline rather than passing
/// quietly. The overlap is a precondition of the assertion, not an observation made after it.
/// </para>
/// </remarks>
public sealed class ResilientAgentFilesClientConcurrencyTests
{
    /// <summary>Permission bits the writes ask for; 0644, the same spelling production uses.</summary>
    private const uint FileMode = 0b110_100_100;

    /// <summary>Deadline for the pair, so a serialising decorator fails instead of hanging the run.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Two writes to one account are inside the agent client at the same time.</summary>
    [Fact]
    public async Task Two_writes_to_one_account_are_inside_the_agent_client_at_the_same_time()
    {
        var inner = new GatedAgentFilesClient(2);
        var client = new ResilientAgentFilesClient(
            inner, OperationPipelineRegistry.WithOperationTimeout(30));

        var first = client.WriteFileAsync("acc1", "public_html/a.txt", "first", FileMode, default);
        var second = client.WriteFileAsync("acc1", "public_html/b.txt", "second", FileMode, default);

        var results = await Task.WhenAll(first, second).WaitAsync(TestTimeout);

        Assert.True(results[0].IsSuccess);
        Assert.True(results[1].IsSuccess);

        // The value, not a bound: one would mean the calls took turns, which is the outcome this
        // test exists to tell apart from overlapping.
        Assert.Equal(2, inner.PeakConcurrency);
    }

    /// <summary>A write and a delete on one account are inside the agent client at the same time.</summary>
    /// <remarks>
    /// The pair that matters is not two of one operation but two DIFFERENT operations, because a
    /// gate someone added would most plausibly be keyed on the account and cover every method on
    /// the client. Asserted separately for the reason the retry policy is asserted from both ends:
    /// each of the two tests is blind to the arrangement the other refuses.
    /// </remarks>
    [Fact]
    public async Task A_write_and_a_delete_on_one_account_are_inside_the_agent_client_at_the_same_time()
    {
        var inner = new GatedAgentFilesClient(2);
        var client = new ResilientAgentFilesClient(
            inner, OperationPipelineRegistry.WithOperationTimeout(30));

        var write = client.WriteFileAsync("acc1", "public_html/a.txt", "first", FileMode, default);
        var delete = client.DeleteEntryAsync("acc1", "public_html/b.txt", false, default);

        var written = await write.WaitAsync(TestTimeout);
        var deleted = await delete.WaitAsync(TestTimeout);

        Assert.True(written.IsSuccess);
        Assert.True(deleted.IsSuccess);
        Assert.Equal(2, inner.PeakConcurrency);
    }
}
