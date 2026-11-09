using Maran.Host.Cli.Commands;
using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Models;
using Maran.Host.Tests.Cli.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.Cli.Commands;

/// <summary>
/// The argument gates of <c>report-update</c>, which run before anything is resolved or written. The
/// report itself is exercised against a real database in the integration tests.
/// </summary>
public sealed class ReportUpdateCliCommandTests
{
    /// <summary>Builds the verb over an empty container: the gates under test must never resolve the reporter.</summary>
    /// <returns>The verb.</returns>
    private static ReportUpdateCliCommand Verb()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        return new ReportUpdateCliCommand(provider.GetRequiredService<IServiceScopeFactory>());
    }

    /// <summary>Builds an invocation of the verb.</summary>
    /// <param name="arguments">The verb's arguments.</param>
    /// <returns>The invocation.</returns>
    private static CliInvocation Invocation(params string[] arguments)
    {
        return new CliInvocation("report-update", arguments, false, false, null);
    }

    /// <summary>A missing, extra, unknown or hostile argument is a usage error and writes nothing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("applied 1.2.3")]
    [InlineData("applied 1.2.3 1.2.4 extra")]
    [InlineData("succeeded 1.2.3 1.2.4")]
    [InlineData("APPLIED 1.2.3 1.2.4")]
    [InlineData("--applied 1.2.3 1.2.4")]
    [InlineData("applied ../1.2.3 1.2.4")]
    [InlineData("rolled-back 1.2.3 1.2.4;reboot")]
    public async Task A_malformed_command_line_is_a_usage_error(string commandLine)
    {
        using var console = new CapturedCliConsole();
        var arguments = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var result = await Verb().ExecuteAsync(Invocation(arguments), console.Console, default);

        Assert.Equal(CliExitCode.Usage, result.ExitCode);
        Assert.Equal(string.Empty, console.Output);
        Assert.Contains("usage", console.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A hostile release name with a newline is refused even though it splits into the right number of words.</summary>
    [Fact]
    public async Task A_release_name_with_a_newline_is_a_usage_error()
    {
        using var console = new CapturedCliConsole();

        var result = await Verb().ExecuteAsync(
            Invocation("rolled-back", "1.2.3\nBcc: x@example.com", "1.2.4"), console.Console, default);

        Assert.Equal(CliExitCode.Usage, result.ExitCode);
    }

    /// <summary>
    /// Every outcome word with plain names passes the gates and goes on to resolve the reporter (the
    /// inverse control: an empty container cannot supply it, so reaching it is the observable).
    /// </summary>
    [Theory]
    [InlineData("applied")]
    [InlineData("rolled-back")]
    [InlineData("rollback-failed")]
    [InlineData("interrupted")]
    public async Task A_valid_command_line_passes_the_gates_and_asks_for_the_reporter(string outcome)
    {
        using var console = new CapturedCliConsole();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await Verb().ExecuteAsync(Invocation(outcome, "1.2.3", "1.2.4"), console.Console, default);
        });
    }
}
