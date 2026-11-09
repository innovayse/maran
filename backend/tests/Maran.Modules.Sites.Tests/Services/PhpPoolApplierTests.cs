using Maran.Agent.Client.Services.PhpService;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sites.Tests.Services;

/// <summary>Behavioral contract of <see cref="PhpPoolApplier"/>.</summary>
public sealed class PhpPoolApplierTests
{
    /// <summary>The new settings of each version are the ones sent.</summary>
    private static readonly IReadOnlyList<PhpSettingDto> Desired = [new("memory_limit", "64M")];

    /// <summary>The settings the pools carried before.</summary>
    private static readonly IReadOnlyList<PhpSettingDto> Before = [new("memory_limit", "32M")];

    /// <summary>Every version is written with the budget and the settings.</summary>
    [Fact]
    public async Task Every_version_is_written_with_the_budget_and_the_settings()
    {
        var php = new RecordingAgentPhpClient();

        var result = await new PhpPoolApplier(php).ApplyAsync("acme", 7, ["8.3", "8.4"], Desired, Before, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, php.PoolCalls.Count);
        Assert.All(php.PoolCalls, call => { Assert.Equal(7u, call.MaxChildren); });
    }

    /// <summary>A refusal puts the versions already written back and returns the first failure.</summary>
    [Fact]
    public async Task A_refusal_puts_the_versions_already_written_back()
    {
        var php = new RecordingAgentPhpClient();
        php.PoolFailures["8.4"] = Error.Of("AgentValidationFailed", ErrorType.Validation);

        var result = await new PhpPoolApplier(php).ApplyAsync("acme", 7, ["8.3", "8.4"], Desired, Before, CancellationToken.None);

        Assert.Equal("AgentValidationFailed", result.Error!.Code);
        Assert.Equal(Before, php.PoolCalls[^1].Settings);
        Assert.Equal("8.3", php.PoolCalls[^1].Version);
    }

    /// <summary>Without a previous set nothing is sent back.</summary>
    [Fact]
    public async Task Without_a_previous_set_nothing_is_sent_back()
    {
        var php = new RecordingAgentPhpClient();
        php.PoolFailures["8.4"] = Error.Of("AgentValidationFailed", ErrorType.Validation);

        await new PhpPoolApplier(php).ApplyAsync("acme", 7, ["8.3", "8.4"], Desired, previous: null, CancellationToken.None);

        Assert.Equal(2, php.PoolCalls.Count);
    }

    /// <summary>An account with no pool has nothing written.</summary>
    [Fact]
    public async Task An_account_with_no_pool_has_nothing_written()
    {
        var php = new RecordingAgentPhpClient();

        var result = await new PhpPoolApplier(php).ApplyAsync("acme", 7, [], Desired, Before, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(php.PoolCalls);
    }
}
