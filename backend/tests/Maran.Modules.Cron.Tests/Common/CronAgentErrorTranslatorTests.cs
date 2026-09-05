using Maran.Modules.Cron.Mappers;
using Maran.Modules.Cron.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Cron.Tests.Common;

/// <summary>How an agent failure becomes something this module may carry, and what it may write down.</summary>
public sealed class CronAgentErrorTranslatorTests
{
    private const string EntryId = "3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d";

    /// <summary>A missing entry becomes this modules own not found code.</summary>
    [Fact]
    public void A_missing_entry_becomes_this_modules_own_not_found_code()
    {
        // The suffix is load-bearing: the result translation maps a code ending in NotFound to a 404,
        // which is the answer another tenant's entry must get — never a 403 confirming it exists.
        Assert.Equal("CronEntryNotFound", Translate("AgentNotFound").Code);
    }

    /// <summary>A duplicate the agent refused becomes this modules own conflict code.</summary>
    [Fact]
    public void A_duplicate_the_agent_refused_becomes_this_modules_own_conflict_code()
    {
        Assert.Equal("CronEntryAlreadyExists", Translate("AgentAlreadyExists").Code);
    }

    /// <summary>Every other agent failure collapses to one operator facing code.</summary>
    [Theory]
    [InlineData("AgentUnspecified")]
    [InlineData("AgentInvalidInput")]
    [InlineData("AgentValidationFailed")]
    [InlineData("AgentSystemFailure")]
    [InlineData("AgentInvalidResponse")]
    public void Every_other_agent_failure_collapses_to_one_operator_facing_code(string agentCode)
    {
        // The difference between them is an operator's question, and the log line is where an
        // operator reads it. A customer gets one sentence this module owns and translates.
        Assert.Equal("CronOperationFailed", Translate(agentCode).Code);
    }

    /// <summary>No agent code is ever forwarded to the caller unchanged.</summary>
    [Theory]
    [InlineData("AgentNotFound")]
    [InlineData("AgentAlreadyExists")]
    [InlineData("AgentSystemFailure")]
    [InlineData("SomethingTheAgentGrewLater")]
    public void No_agent_code_is_ever_forwarded_to_the_caller_unchanged(string agentCode)
    {
        // RULING 31 in one assertion, including for a code this module has never heard of: the
        // default arm must re-code it rather than pass it through, or a future agent code would
        // arrive at a customer as an untranslated machine string.
        Assert.NotEqual(agentCode, Translate(agentCode).Code);
    }

    /// <summary>The log line names the operation the subject and the agents code.</summary>
    [Fact]
    public void The_log_line_names_the_operation_the_subject_and_the_agents_code()
    {
        var logger = new CapturingLogger<CronAgentErrorTranslatorTests>();

        CronAgentErrorTranslator.Translate(logger, Error.Of("AgentSystemFailure", ErrorType.Failure), "UpdateEntryAsync", EntryId);

        var line = Assert.Single(logger.Lines);
        Assert.Contains("UpdateEntryAsync", line, StringComparison.Ordinal);
        Assert.Contains(EntryId, line, StringComparison.Ordinal);
        Assert.Contains("AgentSystemFailure", line, StringComparison.Ordinal);
    }

    /// <summary>Translates one agent code through a throwaway logger.</summary>
    /// <param name="agentCode">The code the agent client produced.</param>
    /// <returns>The error this module answers with.</returns>
    private static Error Translate(string agentCode)
    {
        return CronAgentErrorTranslator.Translate(
            new CapturingLogger<CronAgentErrorTranslatorTests>(),
            Error.Of(agentCode, ErrorType.Validation),
            "CreateEntryAsync",
            EntryId);
    }
}
