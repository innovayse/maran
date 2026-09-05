namespace Maran.SharedKernel.Results;

/// <summary>
/// A typed domain failure: a machine-stable code, and the kind of failure it is. The code is the key
/// of the entry in the owning module's <c>Resources/ErrorMessages.resx</c> triple, which is where
/// every word a human reads lives (rules/csharp.md "The backend owns all user-facing message text");
/// the kind, and nothing else, decides the HTTP status (<c>ApiResultExtensions.MapStatusCode</c>).
/// </summary>
/// <remarks>
/// <para>
/// The type deliberately carries no message. A second, English-only string inside the code was a
/// standing invitation to two divergent descriptions of one failure — one translated and shown, one
/// hard-coded and never seen — and to a caller eventually rendering the untranslated one. With only
/// a code, there is exactly one place to write the sentence and exactly one place to translate it.
/// </para>
/// <para>
/// It carries the kind for the mirror-image reason: <see cref="ErrorType"/> explains what the status
/// used to be inferred from and how that failed. There is deliberately NO single-argument factory —
/// an overload that guessed would reintroduce the guess at the one call site that forgot, and the
/// guess is invisible until a customer reads the wrong status.
/// </para>
/// </remarks>
/// <param name="Code">
/// Machine-stable, flat PascalCase, and equal to the resource key of its message. It identifies the
/// failure; it does not classify it — that is <paramref name="Type"/>'s job.
/// </param>
/// <param name="Type">What kind of failure this is, which is what the HTTP status is derived from.</param>
public sealed record Error(string Code, ErrorType Type)
{
    /// <summary>Creates an error, guarding against empty codes.</summary>
    /// <param name="code">The machine-stable code, equal to its resource key.</param>
    /// <param name="type">What kind of failure it is.</param>
    /// <returns>The error carrying that code and kind.</returns>
    public static Error Of(string code, ErrorType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new Error(code, type);
    }
}
