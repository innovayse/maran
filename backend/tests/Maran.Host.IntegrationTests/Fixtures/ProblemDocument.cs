using System.Net;
using System.Text.Json;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Reads the RFC 7807 problem document a refused request carries: its machine code, and the facts the
/// panel attaches to a failure beside it.
/// </summary>
public static class ProblemDocument
{
    /// <summary>Reads the machine <c>code</c> of a refused response.</summary>
    /// <param name="response">The refused response.</param>
    /// <returns>The code.</returns>
    public static async Task<string> CodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString()!;
    }

    /// <summary>
    /// Reads what a response says about itself and nothing about who asked: its status and its code.
    /// Two refusals with the same signature are indistinguishable to the caller, which is what a missing
    /// row and a row another tenant owns must be.
    /// </summary>
    /// <param name="response">The refused response.</param>
    /// <returns>The status and the code.</returns>
    public static async Task<(HttpStatusCode Status, string Code)> SignatureAsync(HttpResponseMessage response)
    {
        return (response.StatusCode, await CodeAsync(response));
    }

    /// <summary>Reads the field names a plan refusal lists under <c>envelope.fields</c>.</summary>
    /// <param name="response">The refused response.</param>
    /// <returns>The names, in the order the panel listed them.</returns>
    public static async Task<IReadOnlyList<string>> EnvelopeFieldsAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("envelope")
            .GetProperty("fields")
            .EnumerateArray()
            .Select(field =>
            {
                return field.GetString()!;
            })
            .ToList();
    }
}
