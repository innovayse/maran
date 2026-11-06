using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Cli.Models;
using Maran.Host.OpenApi;

namespace Maran.Host.Cli.Commands;

/// <summary>
/// Writes the panel's OpenAPI document to a file and exits: <c>Maran.Host --cli openapi &lt;output path&gt;</c>.
/// It starts no listener, opens no database connection and calls no agent — it reads the controllers the
/// container was built from. The document is how the API's contract is committed, diffed and released;
/// there is deliberately no endpoint that serves it.
/// </summary>
public sealed class OpenApiCliCommand : ICliCommand
{
    /// <summary>The word that selects this verb; the OpenAPI registration is keyed on it too.</summary>
    public const string VerbName = "openapi";

    /// <summary>
    /// The writer, or null when the generator was not registered. It is registered only for this verb
    /// (<c>AddPanelOpenApi</c>), so null is a composition fault and is reported, not assumed away.
    /// </summary>
    private readonly OpenApiDocumentWriter? _writer;

    /// <summary>Creates the verb.</summary>
    /// <param name="writer">The writer; absent when the container was composed for another verb.</param>
    public OpenApiCliCommand(OpenApiDocumentWriter? writer = null)
    {
        _writer = writer;
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
        if (invocation.Arguments.Count != 1 || string.IsNullOrWhiteSpace(invocation.Arguments[0]))
        {
            await console.Error.WriteLineAsync("usage: openapi <output path>");
            return new CliResult(CliExitCode.Usage);
        }

        if (_writer is null)
        {
            await console.Error.WriteLineAsync("The OpenAPI generator is not registered in this run.");
            return new CliResult(CliExitCode.Unavailable);
        }

        var path = Path.GetFullPath(invocation.Arguments[0]);
        await _writer.WriteAsync(path, cancellationToken);
        await console.Output.WriteLineAsync($"OpenAPI document written to {path}");
        return new CliResult(CliExitCode.Success);
    }
}
