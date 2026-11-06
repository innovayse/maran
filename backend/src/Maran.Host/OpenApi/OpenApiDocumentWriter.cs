using System.Text;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Swashbuckle.AspNetCore.Swagger;

namespace Maran.Host.OpenApi;

/// <summary>
/// Renders the panel's OpenAPI document and writes it to a file. It listens on nothing: the document is
/// built from the controllers the container already knows, and the only thing that reads it is the
/// <c>--cli openapi</c> verb and the tests.
/// </summary>
public sealed class OpenApiDocumentWriter
{
    /// <summary>The generator the document is read from.</summary>
    private readonly ISwaggerProvider _generator;

    /// <summary>Creates the writer.</summary>
    /// <param name="generator">The generator registered by <c>AddPanelOpenApiGenerator</c>.</param>
    public OpenApiDocumentWriter(ISwaggerProvider generator)
    {
        _generator = generator;
    }

    /// <summary>Renders the document as OpenAPI 3 JSON.</summary>
    /// <returns>
    /// The document with LF line endings and one trailing newline, so the same API is the same bytes
    /// on every machine and a committed copy ends the way a text file does.
    /// </returns>
    public string Render()
    {
        var document = _generator.GetSwagger(OpenApiDocumentNames.Document);
        var json = document.SerializeAsJson(OpenApiSpecVersion.OpenApi3_0);
        return json.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";
    }

    /// <summary>Renders the document and writes it, replacing any file already there.</summary>
    /// <param name="path">Where to write; its directory must exist.</param>
    /// <param name="cancellationToken">Cancelled when the process is interrupted.</param>
    /// <returns>A task that completes when the file is written.</returns>
    public Task WriteAsync(string path, CancellationToken cancellationToken)
    {
        // No byte-order mark: a baseline that began with one would differ from the same text written by
        // any other tool, and the gate compares bytes.
        return File.WriteAllTextAsync(path, Render(), new UTF8Encoding(false), cancellationToken);
    }
}
