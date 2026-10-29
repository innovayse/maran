using Maran.Host.Cli.Commands;
using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Models;
using Maran.Host.Tests.Cli.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.Cli.Commands;

/// <summary>
/// The argument and terminal gates of <c>admin-reset-password</c>, which run before anything touches
/// the database. The recovery itself is exercised against a real database in the integration tests.
/// </summary>
public sealed class AdminResetPasswordCliCommandTests
{
    /// <summary>Builds the verb over an empty container: the gates under test must never resolve the issuer.</summary>
    /// <returns>The verb.</returns>
    private static AdminResetPasswordCliCommand Verb()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        return new AdminResetPasswordCliCommand(provider.GetRequiredService<IServiceScopeFactory>());
    }

    /// <summary>Builds an invocation of the verb.</summary>
    /// <param name="terminal">Whether standard output is a terminal.</param>
    /// <param name="arguments">The verb's arguments.</param>
    /// <returns>The invocation.</returns>
    private static CliInvocation Invocation(bool terminal, params string[] arguments)
    {
        return new CliInvocation("admin-reset-password", arguments, terminal, false, "alice");
    }

    /// <summary>A malformed command line is a usage error and prints nothing on standard output.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("--clear-two-factor")]
    [InlineData("alice bob")]
    [InlineData("alice --bogus")]
    [InlineData("--")]
    [InlineData("-- alice bob")]
    [InlineData("alice -- bob")]
    public async Task A_malformed_command_line_is_a_usage_error(string commandLine)
    {
        using var console = new CapturedCliConsole();
        var arguments = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var result = await Verb().ExecuteAsync(Invocation(true, arguments), console.Console, default);

        Assert.Equal(CliExitCode.Usage, result.ExitCode);
        Assert.Equal(string.Empty, console.Output);
        Assert.Contains("usage", console.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A pipe is refused without the flag: nothing reaches standard output.</summary>
    [Fact]
    public async Task Output_that_is_not_a_terminal_is_refused_without_the_flag()
    {
        using var console = new CapturedCliConsole();

        var result = await Verb().ExecuteAsync(Invocation(false, "alice"), console.Console, default);

        Assert.Equal(CliExitCode.Refused, result.ExitCode);
        Assert.Equal(string.Empty, console.Output);
        Assert.Contains("--allow-non-terminal", console.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// With the flag the terminal gate is passed (inverse control): the verb goes on to ask the container
    /// for the issuer, which this empty container cannot give, so it throws instead of refusing.
    /// </summary>
    [Fact]
    public async Task The_allow_non_terminal_flag_lets_the_verb_past_the_terminal_gate()
    {
        using var console = new CapturedCliConsole();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await Verb().ExecuteAsync(Invocation(false, "alice", "--allow-non-terminal"), console.Console, default);
        });

        Assert.Equal(string.Empty, console.Output);
    }

    /// <summary>
    /// The installed wrapper's own spelling, flags then <c>--</c> then the identifier, passes the argument
    /// gate: the verb goes on to ask the container for the issuer, which this empty container cannot give.
    /// A usage refusal here is the bug that made every real recovery exit 64.
    /// </summary>
    /// <param name="commandLine">The arguments as the wrapper passes them.</param>
    [Theory]
    [InlineData("--allow-non-terminal -- alice")]
    [InlineData("--allow-non-terminal --clear-two-factor -- alice")]
    [InlineData("--allow-non-terminal -- -alice")]
    public async Task The_wrappers_end_of_options_marker_is_accepted(string commandLine)
    {
        using var console = new CapturedCliConsole();
        var arguments = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await Verb().ExecuteAsync(Invocation(false, arguments), console.Console, default);
        });

        Assert.Equal(string.Empty, console.Output);
    }
}
