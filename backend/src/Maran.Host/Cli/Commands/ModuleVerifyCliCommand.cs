using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Models;
using Maran.Packaging.Models;
using Maran.Packaging.Services;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Utilities.Versions;

namespace Maran.Host.Cli.Commands;

/// <summary>
/// Asks the open verification library whether a module package is acceptable:
/// <c>Maran.Host --cli module-verify &lt;package&gt; --pubkey &lt;pem&gt; [--panel-version &lt;semver&gt;]</c>.
/// It answers <c>VERIFIED: &lt;id&gt; &lt;version&gt;</c> or <c>REFUSED: &lt;code&gt;</c> and re-implements no check
/// of its own: the shell tooling and the panel's loader reach the same code.
/// </summary>
/// <remarks>
/// The public key is an argument and never read from the repository: the key that signs modules is not the
/// release key, and the library is built so it cannot be taught to trust a key by editing it. A verb that
/// loaded nothing and installed nothing, it says only whether the bytes would be accepted.
/// </remarks>
public sealed class ModuleVerifyCliCommand : ICliCommand
{
    /// <summary>The word that selects this verb.</summary>
    public const string VerbName = "module-verify";

    /// <summary>The flag that names the module-signing public key.</summary>
    private const string PublicKeyFlag = "--pubkey";

    /// <summary>The flag that overrides the panel version the package is judged against.</summary>
    private const string PanelVersionFlag = "--panel-version";

    /// <summary>The running panel's version, the default the package is judged against.</summary>
    private readonly IPanelVersion _panelVersion;

    /// <summary>The panel's clock, which decides whether a package is valid yet.</summary>
    private readonly IClock _clock;

    /// <summary>Reads the archive safely, with the library's limits.</summary>
    private readonly IpmReader _reader;

    /// <summary>Creates the verb.</summary>
    /// <param name="panelVersion">The running panel's version.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="reader">The archive reader.</param>
    public ModuleVerifyCliCommand(IPanelVersion panelVersion, IClock clock, IpmReader reader)
    {
        _panelVersion = panelVersion;
        _clock = clock;
        _reader = reader;
    }

    /// <inheritdoc />
    public string Verb
    {
        get { return VerbName; }
    }

    /// <inheritdoc />
    public async Task<CliResult> ExecuteAsync(
        CliInvocation invocation,
        CliConsole console,
        CancellationToken cancellationToken)
    {
        if (!TryParse(invocation.Arguments, out var packagePath, out var publicKeyPath, out var panelVersionText))
        {
            await console.Error.WriteLineAsync(
                "usage: module-verify <package> --pubkey <pem file> [--panel-version <semver>]");
            return new CliResult(CliExitCode.Usage);
        }

        if (!SemanticVersion.TryParse(panelVersionText ?? _panelVersion.Current, out var panelVersion))
        {
            await console.Error.WriteLineAsync($"'{panelVersionText}' is not a semantic version.");
            return new CliResult(CliExitCode.Usage);
        }

        if (!File.Exists(packagePath) || !File.Exists(publicKeyPath))
        {
            await console.Error.WriteLineAsync("The package or the public key file does not exist.");
            return new CliResult(CliExitCode.Failure);
        }

        var signatures = IpmSignatureVerifier.FromPem(await File.ReadAllTextAsync(publicKeyPath, cancellationToken));

        if (!signatures.IsSuccess)
        {
            return await RefusedAsync(console, signatures.Error!.Code);
        }

        await using var package = File.OpenRead(packagePath);
        var verified = new IpmPackageVerifier(_reader, signatures.Value).Verify(
            package,
            new IpmVerificationContext(panelVersion!, _clock.UtcNow, null, null));

        if (!verified.IsSuccess)
        {
            return await RefusedAsync(console, verified.Error!.Code);
        }

        await console.Output.WriteLineAsync($"VERIFIED: {verified.Value.Manifest.Id} {verified.Value.Manifest.Version}");
        return new CliResult(CliExitCode.Success);
    }

    /// <summary>Reads the verb's arguments.</summary>
    /// <param name="arguments">Everything after the verb.</param>
    /// <param name="packagePath">The package file.</param>
    /// <param name="publicKeyPath">The public key file.</param>
    /// <param name="panelVersionText">The panel version override, or null when none was given.</param>
    /// <returns>False when the command line is malformed: no package, no key, a flag twice or an unknown flag.</returns>
    private static bool TryParse(
        IReadOnlyList<string> arguments,
        out string packagePath,
        out string publicKeyPath,
        out string? panelVersionText)
    {
        packagePath = string.Empty;
        publicKeyPath = string.Empty;
        panelVersionText = null;

        if (arguments.Count == 0 || arguments[0].StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        packagePath = arguments[0];

        for (var index = 1; index < arguments.Count; index += 2)
        {
            if (index + 1 >= arguments.Count)
            {
                return false;
            }

            var value = arguments[index + 1];

            if (arguments[index] == PublicKeyFlag && publicKeyPath.Length == 0)
            {
                publicKeyPath = value;
            }
            else if (arguments[index] == PanelVersionFlag && panelVersionText is null)
            {
                panelVersionText = value;
            }
            else
            {
                return false;
            }
        }

        return publicKeyPath.Length > 0;
    }

    /// <summary>States a refusal on standard error.</summary>
    /// <param name="console">The streams the verb talks through.</param>
    /// <param name="code">The library's refusal code.</param>
    /// <returns>A failing result: the package is not acceptable.</returns>
    private static async Task<CliResult> RefusedAsync(CliConsole console, string code)
    {
        await console.Error.WriteLineAsync($"REFUSED: {code}");
        return new CliResult(CliExitCode.Failure);
    }
}
