namespace Maran.SharedKernel.Results;

/// <summary>
/// A typed domain failure, identified by nothing but its machine-stable code. The code drives the
/// HTTP status (<c>ApiResultExtensions.MapStatusCode</c>) and is the key of the entry in the owning
/// module's <c>Resources/ErrorMessages.resx</c> triple, which is where every word a human reads
/// lives (rules/csharp.md "The backend owns all user-facing message text").
/// </summary>
/// <remarks>
/// The type deliberately carries no message. A second, English-only string inside the code was a
/// standing invitation to two divergent descriptions of one failure — one translated and shown, one
/// hard-coded and never seen — and to a caller eventually rendering the untranslated one. With only
/// a code, there is exactly one place to write the sentence and exactly one place to translate it.
/// </remarks>
/// <param name="Code">
/// Machine-stable, flat PascalCase, and equal to the resource key of its message. The suffix
/// carries meaning: <c>…NotFound</c> answers 404, <c>…Taken</c>/<c>…AlreadyExists</c> 409,
/// <c>…Forbidden</c> 403, <c>…Unauthorized</c> 401, anything else 400.
/// </param>
public sealed record Error(string Code)
{
    /// <summary>Creates an error, guarding against empty codes.</summary>
    /// <param name="code">The machine-stable code, equal to its resource key.</param>
    /// <returns>The error carrying that code.</returns>
    public static Error Of(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new Error(code);
    }
}
