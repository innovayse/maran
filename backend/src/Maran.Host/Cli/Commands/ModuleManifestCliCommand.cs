using System.Text;
using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Models;
using Maran.Host.Cli.Services;
using Maran.Packaging.Services;

namespace Maran.Host.Cli.Commands;

/// <summary>
/// Prints the manifest of a module assembly as the <c>ipm.json</c> the package builder starts from:
/// <c>Maran.Host --cli module-manifest &lt;assembly&gt;</c>. It starts no listener, opens no database
/// connection and calls no agent; it reads the module the assembly holds (<see cref="ModuleManifestReader"/>).
/// </summary>
/// <remarks>
/// The output carries an empty file listing and no interface bundle: those exist only once the package
/// does, and the builder (<c>maran ipm package</c>) adds them. The manifest is read from the module and
/// never typed, so what a package declares is what the module declares.
/// </remarks>
public sealed class ModuleManifestCliCommand : ICliCommand
{
    /// <summary>The word that selects this verb.</summary>
    public const string VerbName = "module-manifest";

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
        if (invocation.Arguments.Count != 1 || string.IsNullOrWhiteSpace(invocation.Arguments[0]))
        {
            await console.Error.WriteLineAsync("usage: module-manifest <module assembly>");
            return new CliResult(CliExitCode.Usage);
        }

        var path = invocation.Arguments[0];

        if (!File.Exists(path))
        {
            await console.Error.WriteLineAsync($"No such file: {path}");
            return new CliResult(CliExitCode.Failure);
        }

        var reading = ModuleManifestReader.Read(path);

        if (reading.Manifest is null)
        {
            await console.Error.WriteLineAsync(reading.Failure);
            return new CliResult(CliExitCode.Failure);
        }

        await console.Output.WriteLineAsync(Encoding.UTF8.GetString(IpmManifestCodec.Serialize(reading.Manifest)));
        return new CliResult(CliExitCode.Success);
    }
}
