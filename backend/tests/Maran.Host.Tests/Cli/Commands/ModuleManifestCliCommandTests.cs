using System.Text;
using Maran.Host.Cli.Commands;
using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Models;
using Maran.Host.Tests.Cli.Fixtures;
using Maran.Modules.Cron;
using Maran.Modules.Identity;
using Maran.Packaging.Services;

namespace Maran.Host.Tests.Cli.Commands;

/// <summary>
/// <c>module-manifest</c> prints the manifest of the one packageable module an assembly holds, in the form
/// the package builder starts from. The tests read real module assemblies of this panel, so the verb is
/// proven against the shape modules really have and not against a fixture written to please it.
/// </summary>
public sealed class ModuleManifestCliCommandTests
{
    /// <summary>The verb answers to the word the tooling passes.</summary>
    [Fact]
    public void The_verb_answers_to_the_word_the_tooling_passes()
    {
        Assert.Equal("module-manifest", new ModuleManifestCliCommand().Verb);
    }

    /// <summary>A packageable module that owns a schema is printed as a manifest the library reads back.</summary>
    [Fact]
    public async Task A_packageable_module_is_printed_as_a_manifest_the_library_reads_back()
    {
        using var console = new CapturedCliConsole();

        var result = await new ModuleManifestCliCommand().ExecuteAsync(
            Invocation(typeof(CronModule).Assembly.Location),
            console.Console,
            default);

        Assert.Equal(CliExitCode.Success, result.ExitCode);
        var parsed = IpmManifestCodec.Parse(Encoding.UTF8.GetBytes(console.Output));
        Assert.True(parsed.IsSuccess, "The printed manifest is not one the library reads.");
        var manifest = parsed.Value;
        Assert.Equal("cron", manifest.Id);
        Assert.Equal(CronManifest.Instance.Version, manifest.Version);
        Assert.Equal("cron", manifest.SchemaName);
        Assert.Equal("Maran.Modules.Cron.dll", manifest.EntryAssembly);
        Assert.NotNull(manifest.Migrations);
        Assert.Null(manifest.Migrations.From);
        Assert.False(string.IsNullOrWhiteSpace(manifest.Migrations.To));
        Assert.Empty(manifest.Files);
        Assert.Null(manifest.Spa);
    }

    /// <summary>A core module is refused: it is part of the panel and is never a package.</summary>
    [Fact]
    public async Task A_core_module_is_refused()
    {
        using var console = new CapturedCliConsole();

        var result = await new ModuleManifestCliCommand().ExecuteAsync(
            Invocation(typeof(IdentityModule).Assembly.Location),
            console.Console,
            default);

        Assert.Equal(CliExitCode.Failure, result.ExitCode);
        Assert.Equal(string.Empty, console.Output);
        Assert.Contains("never packaged", console.Error, StringComparison.Ordinal);
    }

    /// <summary>An assembly that holds no module is refused.</summary>
    [Fact]
    public async Task An_assembly_with_no_module_is_refused()
    {
        using var console = new CapturedCliConsole();

        var result = await new ModuleManifestCliCommand().ExecuteAsync(
            Invocation(typeof(Maran.SharedKernel.Results.Result).Assembly.Location),
            console.Console,
            default);

        Assert.Equal(CliExitCode.Failure, result.ExitCode);
        Assert.Contains("holds 0 modules", console.Error, StringComparison.Ordinal);
    }

    /// <summary>A file that is not an assembly is refused with a sentence, not a stack trace.</summary>
    [Fact]
    public async Task A_file_that_is_not_an_assembly_is_refused()
    {
        var path = Path.Combine(Path.GetTempPath(), $"maran-not-an-assembly-{Guid.NewGuid():N}.dll");
        await File.WriteAllTextAsync(path, "this is not a PE file");

        try
        {
            using var console = new CapturedCliConsole();

            var result = await new ModuleManifestCliCommand().ExecuteAsync(Invocation(path), console.Console, default);

            Assert.Equal(CliExitCode.Failure, result.ExitCode);
            Assert.Contains("cannot be read as a module assembly", console.Error, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A file that does not exist is a failure naming it.</summary>
    [Fact]
    public async Task A_missing_file_is_a_failure_naming_it()
    {
        using var console = new CapturedCliConsole();

        var result = await new ModuleManifestCliCommand().ExecuteAsync(
            Invocation("/nonexistent/Module.dll"),
            console.Console,
            default);

        Assert.Equal(CliExitCode.Failure, result.ExitCode);
        Assert.Contains("/nonexistent/Module.dll", console.Error, StringComparison.Ordinal);
    }

    /// <summary>No argument at all is a usage error.</summary>
    [Fact]
    public async Task No_argument_is_a_usage_error()
    {
        using var console = new CapturedCliConsole();

        var result = await new ModuleManifestCliCommand().ExecuteAsync(Invocation(), console.Console, default);

        Assert.Equal(CliExitCode.Usage, result.ExitCode);
    }

    /// <summary>Anything but exactly one argument is a usage error.</summary>
    [Theory]
    [InlineData("a.dll", "b.dll")]
    [InlineData("")]
    public async Task Anything_but_one_argument_is_a_usage_error(params string[] arguments)
    {
        using var console = new CapturedCliConsole();

        var result = await new ModuleManifestCliCommand().ExecuteAsync(Invocation(arguments), console.Console, default);

        Assert.Equal(CliExitCode.Usage, result.ExitCode);
        Assert.Contains("usage", console.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Builds an invocation of the verb.</summary>
    /// <param name="arguments">The verb's arguments.</param>
    /// <returns>The invocation.</returns>
    private static CliInvocation Invocation(params string[] arguments)
    {
        return new CliInvocation("module-manifest", arguments, true, false, null);
    }
}
