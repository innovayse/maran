using System.Globalization;
using System.Resources;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Simulated.Errors;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Simulated.Tests.Errors;

/// <summary>The codes and kinds the simulated agent fails with are the real agent client's.</summary>
public sealed class SimulatedAgentErrorsTests
{
    /// <summary>The embedded resource base name of the agent client's error table.</summary>
    private const string ErrorMessagesBaseName = "Maran.Agent.Client.Resources.ErrorMessages";

    /// <summary>
    /// Every code constant the simulation spells exists as a key in the agent client's own error table.
    /// </summary>
    /// <remarks>
    /// The table is internal to the agent client, so the simulation cannot reach its generated names; this
    /// reads the same resource the client does. The search is only worth its answer if it can find a key,
    /// which the control below proves.
    /// </remarks>
    [Fact]
    public void Every_code_the_simulation_raises_is_a_key_of_the_agent_clients_error_table()
    {
        var table = new ResourceManager(ErrorMessagesBaseName, typeof(IAgentDbClient).Assembly);

        var codes = Codes();

        Assert.NotEmpty(codes);
        foreach (var code in codes)
        {
            Assert.NotNull(table.GetString(code, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>The table lookup finds nothing for a code that is not a key, so the test above can fail.</summary>
    [Fact]
    public void The_error_table_lookup_finds_nothing_for_a_code_that_does_not_exist()
    {
        var table = new ResourceManager(ErrorMessagesBaseName, typeof(IAgentDbClient).Assembly);

        Assert.Null(table.GetString("AgentThisCodeIsNotInTheTable", CultureInfo.InvariantCulture));
    }

    /// <summary>The nine failures carry the kind the real client assigns to the same refusal.</summary>
    [Fact]
    public void Each_failure_carries_the_kind_the_real_client_assigns_to_the_same_refusal()
    {
        Assert.Equal(ErrorType.Conflict, SimulatedAgentErrors.AlreadyExists().Type);
        Assert.Equal(ErrorType.NotFound, SimulatedAgentErrors.NotFound().Type);
        Assert.Equal(ErrorType.Validation, SimulatedAgentErrors.InvalidInput().Type);
        Assert.Equal(ErrorType.Validation, SimulatedAgentErrors.ValidationFailed().Type);
        Assert.Equal(ErrorType.Conflict, SimulatedAgentErrors.LimitReached().Type);
        Assert.Equal(ErrorType.Conflict, SimulatedAgentErrors.AccountSuspended().Type);
        Assert.Equal(ErrorType.Unavailable, SimulatedAgentErrors.NotImplemented().Type);
        Assert.Equal(ErrorType.Unavailable, SimulatedAgentErrors.BelowFreeSpaceFloor().Type);
        Assert.Equal(ErrorType.Failure, SimulatedAgentErrors.FirewallPortsMisconfigured().Type);
    }

    /// <summary>Each failure's code is the constant of the same name.</summary>
    [Fact]
    public void Each_failure_carries_the_code_of_its_constant()
    {
        Assert.Equal(SimulatedAgentErrors.AlreadyExistsCode, SimulatedAgentErrors.AlreadyExists().Code);
        Assert.Equal(SimulatedAgentErrors.NotFoundCode, SimulatedAgentErrors.NotFound().Code);
        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, SimulatedAgentErrors.InvalidInput().Code);
        Assert.Equal(SimulatedAgentErrors.ValidationFailedCode, SimulatedAgentErrors.ValidationFailed().Code);
        Assert.Equal(SimulatedAgentErrors.LimitReachedCode, SimulatedAgentErrors.LimitReached().Code);
        Assert.Equal(SimulatedAgentErrors.AccountSuspendedCode, SimulatedAgentErrors.AccountSuspended().Code);
        Assert.Equal(SimulatedAgentErrors.NotImplementedCode, SimulatedAgentErrors.NotImplemented().Code);
        Assert.Equal(SimulatedAgentErrors.BelowFreeSpaceFloorCode, SimulatedAgentErrors.BelowFreeSpaceFloor().Code);
        Assert.Equal(
            SimulatedAgentErrors.FirewallPortsMisconfiguredCode,
            SimulatedAgentErrors.FirewallPortsMisconfigured().Code);
    }

    /// <summary>
    /// Reads every public string constant of the errors type, which is the full list of codes it can raise.
    /// </summary>
    /// <returns>The codes.</returns>
    private static List<string> Codes()
    {
        return typeof(SimulatedAgentErrors)
            .GetFields()
            .Where(field => { return field.IsLiteral && field.FieldType == typeof(string); })
            .Select(field => { return (string)field.GetRawConstantValue()!; })
            .ToList();
    }
}
