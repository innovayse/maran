using Maran.Modules.Files.Mappers;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Files.Tests.Mappers;

/// <summary>Holds <see cref="FilesAgentErrorTranslator"/> to re-coding every agent failure as the module's own.</summary>
public sealed class FilesAgentErrorTranslatorTests
{
    /// <summary>Each agent code the module has a sentence for maps to it, with its kind.</summary>
    /// <param name="agentCode">The agent client's code.</param>
    /// <param name="expectedCode">The module's code.</param>
    /// <param name="expectedType">The module's kind.</param>
    [Theory]
    [InlineData("AgentNotFound", "FilesEntryNotFound", ErrorType.NotFound)]
    [InlineData("AgentAlreadyExists", "FilesEntryExists", ErrorType.Conflict)]
    [InlineData("AgentQuotaExceeded", "FilesQuotaExceeded", ErrorType.Conflict)]
    [InlineData("AgentBelowFreeSpaceFloor", "FilesQuotaExceeded", ErrorType.Conflict)]
    [InlineData("AgentTooLarge", "FilesDownloadTooLarge", ErrorType.Validation)]
    [InlineData("AgentArchiveUnsafe", "FilesArchiveRefused", ErrorType.Validation)]
    [InlineData("AgentLimitReached", "FilesArchiveRefused", ErrorType.Validation)]
    [InlineData("AgentAccountSuspended", "FilesAccountSuspended", ErrorType.Conflict)]
    [InlineData("AgentSaturated", "FilesServerBusy", ErrorType.Unavailable)]
    [InlineData("AgentInvalidInput", "FilesPathInvalid", ErrorType.Validation)]
    [InlineData("AgentSystemFailure", "FilesAgentFailed", ErrorType.Failure)]
    [InlineData("AgentSomethingNew", "FilesAgentFailed", ErrorType.Failure)]
    public void An_agent_code_becomes_the_modules_own(string agentCode, string expectedCode, ErrorType expectedType)
    {
        var translated = FilesAgentErrorTranslator.Translate(
            NullLogger.Instance, Error.Of(agentCode, ErrorType.Failure), "list", "alpha:a");

        Assert.Equal(expectedCode, translated.Code);
        Assert.Equal(expectedType, translated.Type);
    }

    /// <summary>An agent that could not be reached passes through unchanged, as a retryable outage.</summary>
    [Fact]
    public void An_unreachable_agent_passes_through_as_unavailable()
    {
        var translated = FilesAgentErrorTranslator.Translate(
            NullLogger.Instance, Error.Of("AgentUnavailable", ErrorType.Unavailable), "list", "alpha");

        Assert.Equal("AgentUnavailable", translated.Code);
        Assert.Equal(ErrorType.Unavailable, translated.Type);
    }
}
