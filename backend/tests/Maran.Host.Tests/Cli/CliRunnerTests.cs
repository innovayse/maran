using Maran.Host.Cli;
using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Models;
using Maran.Host.Cli.Services;
using Maran.Host.Tests.Cli.Fixtures;
using Microsoft.Extensions.Configuration;

namespace Maran.Host.Tests.Cli;

/// <summary>Dispatch, the root refusal and the translation of failures into exit statuses.</summary>
public sealed class CliRunnerTests
{
    /// <summary>A database password long enough to be searched for by the redactor.</summary>
    private const string DatabasePassword = "hunter2-hunter2-hunter2";

    /// <summary>Builds the literals source around a known database password.</summary>
    /// <returns>The source.</returns>
    private static CliSecretLiterals Secrets()
    {
        return new CliSecretLiterals(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Password"] = DatabasePassword })
                .Build());
    }

    /// <summary>Builds an invocation with the given process facts.</summary>
    /// <param name="verb">The verb.</param>
    /// <param name="runningAsRoot">Whether the process is privileged.</param>
    /// <returns>The invocation.</returns>
    private static CliInvocation Invocation(string verb, bool runningAsRoot = false)
    {
        return new CliInvocation(verb, [], true, runningAsRoot, null);
    }

    /// <summary>Running as root is refused with 77 and the verb never runs.</summary>
    [Fact]
    public async Task Running_as_root_is_refused_and_the_verb_never_runs()
    {
        var verb = new ScriptedCliCommand("status", CliExitCode.Success);
        var runner = new CliRunner([verb], Secrets());
        using var console = new CapturedCliConsole();

        var status = await runner.RunAsync(Invocation("status", runningAsRoot: true), console.Console, default);

        Assert.Equal((int)CliExitCode.PermissionDenied, status);
        Assert.Equal(0, verb.Runs);
        Assert.Contains("root", console.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An unprivileged process runs the verb and returns its own status (inverse control).</summary>
    [Fact]
    public async Task An_unprivileged_process_runs_the_verb_and_exits_with_its_status()
    {
        var verb = new ScriptedCliCommand("status", CliExitCode.Failure);
        var runner = new CliRunner([verb], Secrets());
        using var capture = new CapturedCliConsole();

        var status = await runner.RunAsync(Invocation("status"), capture.Console, default);

        Assert.Equal((int)CliExitCode.Failure, status);
        Assert.Equal(1, verb.Runs);
    }

    /// <summary>An unknown or empty verb is a usage error that lists the known ones.</summary>
    [Theory]
    [InlineData("nonsense")]
    [InlineData("")]
    public async Task An_unknown_or_missing_verb_is_a_usage_error_naming_the_known_verbs(string verb)
    {
        var runner = new CliRunner([new ScriptedCliCommand("status", CliExitCode.Success)], Secrets());
        using var console = new CapturedCliConsole();

        var status = await runner.RunAsync(Invocation(verb), console.Console, default);

        Assert.Equal((int)CliExitCode.Usage, status);
        Assert.Contains("status", console.Error, StringComparison.Ordinal);
    }

    /// <summary>A verb that throws exits 1 and says why on standard error, scrubbed of configured secrets.</summary>
    [Fact]
    public async Task A_verb_that_throws_exits_with_failure_and_the_message_is_scrubbed_of_configured_secrets()
    {
        var failure = new InvalidOperationException($"connection failed for Password={DatabasePassword}");
        var runner = new CliRunner([new ScriptedCliCommand("status", CliExitCode.Success, failure)], Secrets());
        using var console = new CapturedCliConsole();

        var status = await runner.RunAsync(Invocation("status"), console.Console, default);

        Assert.Equal((int)CliExitCode.Failure, status);
        Assert.Contains("InvalidOperationException", console.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(DatabasePassword, console.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, console.Output);
    }

    /// <summary>An interrupted run is a temporary failure, not a crash.</summary>
    [Fact]
    public async Task An_interrupted_run_exits_with_a_temporary_failure()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var failure = new OperationCanceledException(source.Token);
        var runner = new CliRunner([new ScriptedCliCommand("status", CliExitCode.Success, failure)], Secrets());
        using var capture = new CapturedCliConsole();

        var status = await runner.RunAsync(Invocation("status"), capture.Console, source.Token);

        Assert.Equal((int)CliExitCode.TemporaryFailure, status);
    }
}
