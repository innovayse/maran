using Maran.Host.Cli.Commands;
using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Models;
using Maran.Host.Tests.Cli.Fixtures;
using Maran.Host.Tests.OpenApi.Fixtures;

namespace Maran.Host.Tests.Cli.Commands;

/// <summary>
/// The <c>openapi</c> verb: its argument gate, its refusal when the generator is absent, and the file it writes.
/// </summary>
public sealed class OpenApiCliCommandTests : IClassFixture<OpenApiDocumentFixture>
{
    /// <summary>The two hosts and their documents.</summary>
    private readonly OpenApiDocumentFixture _documents;

    /// <summary>Captures the shared fixture.</summary>
    /// <param name="documents">The booted hosts and their rendered documents.</param>
    public OpenApiCliCommandTests(OpenApiDocumentFixture documents)
    {
        _documents = documents;
    }

    /// <summary>Builds an invocation of the verb.</summary>
    /// <param name="arguments">The verb's arguments.</param>
    /// <returns>The invocation.</returns>
    private static CliInvocation Invocation(params string[] arguments)
    {
        return new CliInvocation("openapi", arguments, true, false, null);
    }

    /// <summary>Anything but exactly one path is a usage error that writes nothing to standard output.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("a.json b.json")]
    public async Task Anything_but_exactly_one_path_is_a_usage_error(string commandLine)
    {
        using var console = new CapturedCliConsole();
        var arguments = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var result = await new OpenApiCliCommand().ExecuteAsync(Invocation(arguments), console.Console, default);

        Assert.Equal(CliExitCode.Usage, result.ExitCode);
        Assert.Equal(string.Empty, console.Output);
        Assert.Contains("usage", console.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// With no generator registered the verb reports it unavailable instead of writing an empty file.
    /// </summary>
    [Fact]
    public async Task A_run_composed_without_the_generator_reports_it_unavailable()
    {
        using var console = new CapturedCliConsole();

        var result = await new OpenApiCliCommand().ExecuteAsync(Invocation("out.json"), console.Console, default);

        Assert.Equal(CliExitCode.Unavailable, result.ExitCode);
        Assert.False(File.Exists("out.json"));
    }

    /// <summary>
    /// With the generator the verb writes exactly the rendered document to the given path (inverse control).
    /// </summary>
    [Fact]
    public async Task The_verb_writes_exactly_the_rendered_document_to_the_given_path()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "openapi.json");
            using var console = new CapturedCliConsole();

            var result = await new OpenApiCliCommand(_documents.Writer).ExecuteAsync(
                Invocation(path),
                console.Console,
                default);

            Assert.Equal(CliExitCode.Success, result.ExitCode);
            Assert.Equal(_documents.First, await File.ReadAllTextAsync(path));
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "The file starts with a byte-order mark.");
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
