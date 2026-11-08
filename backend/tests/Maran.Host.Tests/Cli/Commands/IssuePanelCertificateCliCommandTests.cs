using Maran.Host.Cli.Commands;
using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Models;
using Maran.Host.Tests.Cli.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.Cli.Commands;

/// <summary>
/// The argument gate of <c>issue-panel-certificate</c>, which runs before anything touches the
/// database or the authority. The issuance itself is covered in the Ssl module's tests.
/// </summary>
public sealed class IssuePanelCertificateCliCommandTests
{
    /// <summary>Builds the verb over an empty container: the gate under test must never resolve the issuer.</summary>
    /// <returns>The verb.</returns>
    private static IssuePanelCertificateCliCommand Verb()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        return new IssuePanelCertificateCliCommand(provider.GetRequiredService<IServiceScopeFactory>());
    }

    /// <summary>Builds an invocation of the verb.</summary>
    /// <param name="arguments">The verb's arguments.</param>
    /// <returns>The invocation.</returns>
    private static CliInvocation Invocation(params string[] arguments)
    {
        return new CliInvocation("issue-panel-certificate", arguments, true, false, "alice");
    }

    /// <summary>The verb answers to the word the wrapper passes.</summary>
    [Fact]
    public void The_verb_answers_to_the_word_the_wrapper_passes()
    {
        Assert.Equal("issue-panel-certificate", Verb().Verb);
    }

    /// <summary>Any argument is a usage error: the host name is configuration and is never typed.</summary>
    [Theory]
    [InlineData("panel.example.net")]
    [InlineData("--force")]
    [InlineData("--")]
    public async Task Any_argument_is_a_usage_error_because_the_host_name_is_never_typed(string argument)
    {
        using var console = new CapturedCliConsole();

        var result = await Verb().ExecuteAsync(Invocation(argument), console.Console, default);

        Assert.Equal(CliExitCode.Usage, result.ExitCode);
        Assert.Equal(string.Empty, console.Output);
        Assert.Contains("usage", console.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// With no argument the gate is passed (inverse control): the verb goes on to ask the container for
    /// the issuer, which this empty container cannot give, so it throws instead of refusing.
    /// </summary>
    [Fact]
    public async Task With_no_argument_the_verb_goes_on_to_the_issuer()
    {
        using var console = new CapturedCliConsole();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await Verb().ExecuteAsync(Invocation(), console.Console, default);
        });

        Assert.Equal(string.Empty, console.Output);
    }
}
