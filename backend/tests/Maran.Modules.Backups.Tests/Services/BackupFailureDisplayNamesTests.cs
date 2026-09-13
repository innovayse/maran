using System.Globalization;
using Maran.Agent.Client.Services.BackupService;
using Maran.Agent.V1;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Tests.TestSupport;

namespace Maran.Modules.Backups.Tests.Services;

/// <summary>
/// Covers the operator-facing name of a failed backup's code, and — the part that makes this a fix
/// rather than two translations — that EVERY code a row can record has one.
/// </summary>
/// <remarks>
/// The pinned defect, measured in a browser: the backups table rendered a failed row as
/// "Не удалась AgentSystemFailure" — the machine constant, verbatim, in the middle of localized
/// Russian. Naming the two codes that happened to be on screen would not have been a fix, because a
/// code with no entry falls back to the raw string and IS the defect; so the closed sets the codes
/// come from are walked here rather than sampled.
/// </remarks>
public sealed class BackupFailureDisplayNamesTests
{
    /// <summary>
    /// The codes written straight onto a row, outside either stream translator. They are literals in
    /// the test for the same reason they are literals in the code: nothing enumerates them, so this
    /// list is the only place the set is stated, and a code added without an entry here is a code
    /// whose display name nobody demanded.
    /// </summary>
    private static readonly string[] CodesWrittenOutsideTheTranslators =
    [
        "BackupTruncated",
        "BackupInvalidOutcome",
        "BackupScheduledRunAborted",
    ];

    /// <summary>The wire codes the client turns into a stream ENDING rather than into a code.</summary>
    /// <remarks>
    /// <c>ToTerminalCreateEvent</c> answers these with <c>Dropped</c> and <c>Idle</c>, whose codes are
    /// covered by the create-ending walk below. They never reach a row as an <c>Agent*</c> code, so
    /// demanding an entry for one would be demanding a name for something unreachable.
    /// </remarks>
    private static readonly ErrorCode[] CodesThatBecomeStreamEndings =
    [
        ErrorCode.StreamDropped,
        ErrorCode.StreamIdle,
    ];

    /// <summary>Every ending of a create stream has a name an operator can read.</summary>
    /// <remarks>
    /// The first closed set: <see cref="BackupCreateEventKind"/>, walked through the very translator
    /// that decides what a failed row records. A kind added to the client without a name here fails
    /// this test rather than reaching a table cell as a constant.
    /// </remarks>
    [Fact]
    public void Every_create_stream_ending_has_a_name_an_operator_can_read()
    {
        var names = BackupsTestContext.FailureNames();
        var unnamed = new List<string>();

        foreach (var kind in Enum.GetValues<BackupCreateEventKind>())
        {
            if (kind is BackupCreateEventKind.Progress or BackupCreateEventKind.Created)
            {
                continue;
            }

            var code = BackupAgentErrorTranslator.ToFailureCode(
                new BackupCreateEvent(kind, 0, string.Empty, 0, string.Empty, 0, null));

            if (string.Equals(names.Of(code), code, StringComparison.Ordinal))
            {
                unnamed.Add(code);
            }
        }

        Assert.Empty(unnamed);
    }

    /// <summary>Every wire error code the agent can refuse with has a name an operator can read.</summary>
    /// <remarks>
    /// The second closed set: the contract's own <see cref="ErrorCode"/>. The key scheme is restated
    /// here — <c>BackupFailureAgent&lt;Member&gt;</c> — and that restatement is the point: it mirrors
    /// <c>AgentErrorTranslator.ToErrorCode</c>, which is internal to the agent client, so an arm
    /// added there without an entry here shows up as a missing name rather than as a raw code on a
    /// screen. A contract that grows a tenth error code fails this test on the day it is generated.
    /// </remarks>
    [Fact]
    public void Every_wire_error_code_has_a_name_an_operator_can_read()
    {
        var names = BackupsTestContext.FailureNames();
        var unnamed = new List<string>();

        foreach (var wireCode in Enum.GetValues<ErrorCode>())
        {
            if (Array.IndexOf(CodesThatBecomeStreamEndings, wireCode) >= 0)
            {
                continue;
            }

            var code = "Agent" + wireCode;

            if (string.Equals(names.Of(code), code, StringComparison.Ordinal))
            {
                unnamed.Add(code);
            }
        }

        Assert.Empty(unnamed);
    }

    /// <summary>The codes written onto a row outside either translator have names too.</summary>
    [Theory]
    [InlineData("BackupTruncated")]
    [InlineData("BackupInvalidOutcome")]
    [InlineData("BackupScheduledRunAborted")]
    [InlineData("AgentInvalidResponse")]
    public void A_code_written_outside_the_translators_has_a_name_an_operator_can_read(string code)
    {
        var names = BackupsTestContext.FailureNames();

        Assert.NotEqual(code, names.Of(code));
    }

    /// <summary>The list of codes written outside the translators is not empty.</summary>
    /// <remarks>
    /// Guards the theory above from being quietly emptied: a list that lost its entries would leave
    /// a passing suite that checks nothing (rules/testing.md).
    /// </remarks>
    [Fact]
    public void The_codes_written_outside_the_translators_are_actually_listed()
    {
        Assert.NotEmpty(CodesWrittenOutsideTheTranslators);
    }

    /// <summary>The name is the sentence, stated exactly, not merely some string.</summary>
    /// <remarks>
    /// A test asserting "a name came back" would pass against a table full of the raw codes, which
    /// is the defect. The VALUE is asserted (rules/testing.md).
    /// </remarks>
    [Fact]
    public void The_english_name_of_a_server_side_failure_is_the_sentence_it_should_be()
    {
        var names = BackupsTestContext.FailureNames();

        Assert.Equal("The server could not complete the backup", names.Of("AgentSystemFailure"));
    }

    /// <summary>In Russian the name is Russian, which is the whole of what was measured as broken.</summary>
    /// <remarks>
    /// The exact row from the live run: <c>AgentSystemFailure</c>, rendered beside the word
    /// "Не удалась". The assertion is on the Russian VALUE, so a build that resolved the neutral
    /// English text — the state a missing <c>.ru</c> entry silently produces — fails here.
    /// </remarks>
    [Fact]
    public void The_russian_name_of_a_server_side_failure_is_russian()
    {
        var names = BackupsTestContext.FailureNames();
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ru");

            Assert.Equal("Серверу не удалось создать копию", names.Of("AgentSystemFailure"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>A row that did not fail is named nothing at all, never the empty key's fallback.</summary>
    [Fact]
    public void A_backup_that_did_not_fail_is_named_nothing()
    {
        var names = BackupsTestContext.FailureNames();

        Assert.Equal(string.Empty, names.Of(string.Empty));
    }

    /// <summary>A code this build has never heard of falls back to itself rather than to a key.</summary>
    /// <remarks>
    /// The fallback is deliberate and is exactly what the table showed before this type existed. What
    /// must NOT happen is the resx key leaking out — <c>BackupFailureWhatever</c> is worse than the
    /// code, because it names a file nobody outside this repository can read.
    /// </remarks>
    [Fact]
    public void A_code_this_build_has_never_heard_of_falls_back_to_itself()
    {
        var names = BackupsTestContext.FailureNames();

        Assert.Equal("SomeMarketplaceModuleFailure", names.Of("SomeMarketplaceModuleFailure"));
    }
}
