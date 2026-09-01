using System.Text.Json;

namespace Maran.Modules.Ssl.Common;

/// <summary>One successful ACME response: its parsed body and the resource URL it created.</summary>
/// <remarks>
/// The <c>Location</c> header is carried alongside the body because ACME puts identity there rather
/// than in the document — a new order's own URL, and a new account's <c>kid</c>, exist only as
/// headers. A caller that read the body alone would have the order's contents and no way to poll it.
/// </remarks>
/// <param name="Body">The parsed JSON body, or an undefined element when the response had none.</param>
/// <param name="Location">The <c>Location</c> header, or the empty string when the response carried none.</param>
public sealed record AcmeResponse(JsonElement Body, string Location);
