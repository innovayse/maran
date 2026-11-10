using System.Net;
using System.Text.Json;

namespace Maran.Host.IntegrationTests.Marketplace;

/// <summary>What the panel answered one call: the status and the exact text of the body.</summary>
/// <param name="Status">The HTTP status.</param>
/// <param name="Text">The body, as the panel wrote it.</param>
public sealed record PanelAnswer(HttpStatusCode Status, string Text)
{
    /// <summary>The body parsed as JSON; independent of any document the caller must dispose.</summary>
    /// <returns>The root element.</returns>
    public JsonElement Json()
    {
        using var document = JsonDocument.Parse(Text);
        return document.RootElement.Clone();
    }

    /// <summary>The machine code of the problem the panel answered, which is how failures are told apart.</summary>
    /// <returns>The <c>code</c> member of the problem body.</returns>
    public string Code()
    {
        return Json().GetProperty("code").GetString()!;
    }
}
