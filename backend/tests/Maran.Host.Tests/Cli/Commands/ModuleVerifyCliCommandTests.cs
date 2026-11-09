using System.Security.Cryptography;
using Maran.Host.Cli.Commands;
using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Models;
using Maran.Host.Tests.Cli.Fixtures;
using Maran.Host.Tests.Fixtures;
using Maran.Host.Tests.Modules.Fixtures;
using Maran.Packaging.Options;
using Maran.Packaging.Services;

namespace Maran.Host.Tests.Cli.Commands;

/// <summary>
/// <c>module-verify</c> passes a package to the open verification library and reports its answer: the
/// verb adds no check of its own, so these tests prove that it asks, that it passes the library's refusal
/// on by code, and that a good package is accepted.
/// </summary>
public sealed class ModuleVerifyCliCommandTests : IDisposable
{
    /// <summary>The bytes of a stand-in assembly; the library pins and hashes them like any other.</summary>
    private static readonly byte[] StandInAssembly = [1, 2, 3, 4, 5, 6, 7, 8];

    /// <summary>Where this test's packages and keys are written.</summary>
    private readonly ModulePackageWorkspace _workspace = new();

    /// <summary>The key that signs the packages this test writes.</summary>
    private readonly ECDsa _signer = ModulePackageWorkspace.NewSigningKey();

    /// <summary>Removes the packages and keys.</summary>
    public void Dispose()
    {
        _signer.Dispose();
        _workspace.Dispose();
    }

    /// <summary>The verb answers to the word the tooling passes.</summary>
    [Fact]
    public void The_verb_answers_to_the_word_the_tooling_passes()
    {
        Assert.Equal("module-verify", Verb("1.0.0").Verb);
    }

    /// <summary>A package signed by the named key and pinning the bytes it holds is accepted.</summary>
    [Fact]
    public async Task A_package_signed_by_the_named_key_is_accepted()
    {
        var package = _workspace.WritePackage(_signer, StandInAssembly, StandInAssembly);
        var key = _workspace.WritePublicKey(_signer);
        using var console = new CapturedCliConsole();

        var result = await Verb("1.0.0").ExecuteAsync(Invocation(package, "--pubkey", key), console.Console, default);

        Assert.Equal(CliExitCode.Success, result.ExitCode);
        Assert.Equal($"VERIFIED: tests 1.0.0{Environment.NewLine}", console.Output);
    }

    /// <summary>A package checked against a key that did not sign it is refused for its signature.</summary>
    [Fact]
    public async Task A_package_checked_against_another_key_is_refused_for_its_signature()
    {
        var package = _workspace.WritePackage(_signer, StandInAssembly, StandInAssembly);
        using var other = ModulePackageWorkspace.NewSigningKey();
        var key = _workspace.WritePublicKey(other);
        using var console = new CapturedCliConsole();

        var result = await Verb("1.0.0").ExecuteAsync(Invocation(package, "--pubkey", key), console.Console, default);

        Assert.Equal(CliExitCode.Failure, result.ExitCode);
        Assert.Equal(string.Empty, console.Output);
        Assert.Contains("REFUSED: IpmSignatureInvalid", console.Error, StringComparison.Ordinal);
    }

    /// <summary>An assembly altered after the manifest pinned it is refused for its hash, the signature being intact.</summary>
    [Fact]
    public async Task An_assembly_altered_after_it_was_pinned_is_refused_for_its_hash()
    {
        var altered = StandInAssembly.ToArray();
        altered[0] ^= 0xFF;
        var package = _workspace.WritePackage(_signer, StandInAssembly, altered);
        var key = _workspace.WritePublicKey(_signer);
        using var console = new CapturedCliConsole();

        var result = await Verb("1.0.0").ExecuteAsync(Invocation(package, "--pubkey", key), console.Console, default);

        Assert.Equal(CliExitCode.Failure, result.ExitCode);
        Assert.Contains("REFUSED: IpmHashMismatch", console.Error, StringComparison.Ordinal);
    }

    /// <summary>A panel older than the package needs is refused, whether the version comes from the flag or from the build.</summary>
    [Theory]
    [InlineData("0.9.0", true)]
    [InlineData("0.9.0", false)]
    public async Task A_panel_older_than_the_package_needs_is_refused(string panelVersion, bool fromFlag)
    {
        var package = _workspace.WritePackage(_signer, StandInAssembly, StandInAssembly);
        var key = _workspace.WritePublicKey(_signer);
        using var console = new CapturedCliConsole();
        var arguments = fromFlag
            ? Invocation(package, "--pubkey", key, "--panel-version", panelVersion)
            : Invocation(package, "--pubkey", key);

        var result = await Verb(fromFlag ? "2.0.0" : panelVersion).ExecuteAsync(arguments, console.Console, default);

        Assert.Equal(CliExitCode.Failure, result.ExitCode);
        Assert.Contains("REFUSED: IpmPanelTooOld", console.Error, StringComparison.Ordinal);
    }

    /// <summary>The flag wins over the build's version, in both directions (inverse control of the case above).</summary>
    [Fact]
    public async Task The_panel_version_flag_overrides_the_build_version()
    {
        var package = _workspace.WritePackage(_signer, StandInAssembly, StandInAssembly);
        var key = _workspace.WritePublicKey(_signer);
        using var console = new CapturedCliConsole();

        var result = await Verb("0.9.0").ExecuteAsync(
            Invocation(package, "--pubkey", key, "--panel-version", "1.0.0"),
            console.Console,
            default);

        Assert.Equal(CliExitCode.Success, result.ExitCode);
    }

    /// <summary>A key file that holds no key is refused as such, not as a bad package.</summary>
    [Fact]
    public async Task A_key_file_that_holds_no_key_is_refused_as_a_bad_key()
    {
        var package = _workspace.WritePackage(_signer, StandInAssembly, StandInAssembly);
        var notAKey = _workspace.WriteText("not-a-key.pub", "this is not a PEM file");
        using var console = new CapturedCliConsole();

        var result = await Verb("1.0.0").ExecuteAsync(Invocation(package, "--pubkey", notAKey), console.Console, default);

        Assert.Equal(CliExitCode.Failure, result.ExitCode);
        Assert.Contains("REFUSED: IpmPublicKeyInvalid", console.Error, StringComparison.Ordinal);
    }

    /// <summary>A package or key file that does not exist is a failure with a sentence, not a stack trace.</summary>
    [Fact]
    public async Task A_missing_package_file_is_a_failure()
    {
        var key = _workspace.WritePublicKey(_signer);
        using var console = new CapturedCliConsole();

        var result = await Verb("1.0.0").ExecuteAsync(
            Invocation("/nonexistent/package.ipm", "--pubkey", key),
            console.Console,
            default);

        Assert.Equal(CliExitCode.Failure, result.ExitCode);
        Assert.Contains("does not exist", console.Error, StringComparison.Ordinal);
    }

    /// <summary>A malformed command line is a usage error before anything is read.</summary>
    [Theory]
    [InlineData("package.ipm")]
    [InlineData("package.ipm", "--pubkey")]
    [InlineData("--pubkey", "key.pub")]
    [InlineData("package.ipm", "--pubkey", "key.pub", "--pubkey", "other.pub")]
    [InlineData("package.ipm", "--pubkey", "key.pub", "--unknown", "x")]
    public async Task A_malformed_command_line_is_a_usage_error(params string[] arguments)
    {
        using var console = new CapturedCliConsole();

        var result = await Verb("1.0.0").ExecuteAsync(Invocation(arguments), console.Console, default);

        Assert.Equal(CliExitCode.Usage, result.ExitCode);
        Assert.Contains("usage", console.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>No arguments at all is a usage error.</summary>
    [Fact]
    public async Task No_arguments_is_a_usage_error()
    {
        using var console = new CapturedCliConsole();

        var result = await Verb("1.0.0").ExecuteAsync(Invocation(), console.Console, default);

        Assert.Equal(CliExitCode.Usage, result.ExitCode);
    }

    /// <summary>A panel version that is not a semantic version is a usage error.</summary>
    [Fact]
    public async Task A_panel_version_that_is_not_semantic_is_a_usage_error()
    {
        using var console = new CapturedCliConsole();

        var result = await Verb("1.0.0").ExecuteAsync(
            Invocation("package.ipm", "--pubkey", "key.pub", "--panel-version", "one"),
            console.Console,
            default);

        Assert.Equal(CliExitCode.Usage, result.ExitCode);
    }

    /// <summary>Builds the verb over a panel that reports the given version.</summary>
    /// <param name="panelVersion">The version the panel reports.</param>
    /// <returns>The verb.</returns>
    private static ModuleVerifyCliCommand Verb(string panelVersion)
    {
        return new ModuleVerifyCliCommand(
            new FixedPanelVersion(panelVersion),
            new MovableClock(),
            new IpmReader(IpmLimits.Default));
    }

    /// <summary>Builds an invocation of the verb.</summary>
    /// <param name="arguments">The verb's arguments.</param>
    /// <returns>The invocation.</returns>
    private static CliInvocation Invocation(params string[] arguments)
    {
        return new CliInvocation("module-verify", arguments, true, false, null);
    }
}
