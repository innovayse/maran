using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Errors;

/// <summary>The codes the agent client exposes to modules stay the keys of its error table.</summary>
public sealed class AgentErrorCodesTests
{
    /// <summary>The shared not-found code is the key of the agent client's not-found message.</summary>
    [Fact]
    public void The_not_found_code_is_the_key_of_the_not_found_message()
    {
        Assert.Equal(nameof(ErrorMessages.AgentNotFound), AgentErrorCodes.NotFound);
    }

    /// <summary>The code the translator gives the wire's not-found is the shared constant, and no other code is.</summary>
    [Fact]
    public void The_translator_answers_the_wire_not_found_with_the_shared_code()
    {
        Assert.Equal(AgentErrorCodes.NotFound, AgentErrorTranslator.ToErrorCode(ErrorCode.NotFound));
        Assert.NotEqual(AgentErrorCodes.NotFound, AgentErrorTranslator.ToErrorCode(ErrorCode.AlreadyExists));
    }
}
