using Microsoft.Extensions.Configuration;
using Maran.Host.Cli.Commands;
using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Models;
using Maran.Host.Cli.Services;
using Maran.Host.Tests.Cli.Fixtures;

namespace Maran.Host.Tests.Cli.Commands;

/// <summary>The stdin-to-stdout redaction filter the diagnostics collector pipes every file through.</summary>
public sealed class RedactCliCommandTests
{
    /// <summary>A configured encryption key; never a real one.</summary>
    private const string EncryptionKey = "planted-encryption-key-for-the-test-0001";

    /// <summary>Builds the verb around a known configured encryption key.</summary>
    /// <returns>The verb.</returns>
    private static RedactCliCommand Verb()
    {
        return new RedactCliCommand(new CliSecretLiterals(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:EncryptionKey"] = EncryptionKey })
                .Build()));
    }

    /// <summary>The planted configured secret is removed and unrelated text survives (positive control).</summary>
    [Fact]
    public async Task A_configured_secret_is_removed_from_the_stream_and_other_text_is_kept()
    {
        using var console = new CapturedCliConsole($"started ok\nkey was {EncryptionKey} here\n");

        var result = await Verb().ExecuteAsync(
            new CliInvocation("redact", [], false, false, null), console.Console, default);

        Assert.Equal(CliExitCode.Success, result.ExitCode);
        Assert.DoesNotContain(EncryptionKey, console.Output, StringComparison.Ordinal);
        Assert.Contains("started ok", console.Output, StringComparison.Ordinal);
        Assert.Contains("[redacted]", console.Output, StringComparison.Ordinal);
    }

    /// <summary>Text with no secret passes through byte for byte (the inverse control).</summary>
    [Fact]
    public async Task Text_without_a_secret_passes_through_unchanged()
    {
        using var console = new CapturedCliConsole("nothing secret on this line\n");

        await Verb().ExecuteAsync(new CliInvocation("redact", [], false, false, null), console.Console, default);

        Assert.Equal("nothing secret on this line\n", console.Output);
    }

    /// <summary>The filter takes no arguments, and says so rather than ignoring them.</summary>
    [Fact]
    public async Task Arguments_are_a_usage_error()
    {
        using var console = new CapturedCliConsole("x");

        var result = await Verb().ExecuteAsync(
            new CliInvocation("redact", ["--oops"], false, false, null), console.Console, default);

        Assert.Equal(CliExitCode.Usage, result.ExitCode);
        Assert.Equal(string.Empty, console.Output);
    }
}
