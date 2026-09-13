using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>
/// An <see cref="IErrorTextProvider"/> double that renders a code as a sentence a test can
/// recognise, so a test can tell "the code was resolved" from "the code was passed through".
/// </summary>
/// <remarks>
/// The distinction matters: an error code leaking to a customer in place of a sentence is a real
/// defect this repository has shipped before, and a double that returned the code unchanged would
/// make that defect invisible.
/// </remarks>
public sealed class StubErrorTextProvider : IErrorTextProvider
{
    /// <summary>The codes this provider was asked to resolve, in order.</summary>
    public List<string> Resolved { get; } = [];

    /// <inheritdoc />
    public string Resolve(string code, params object[] arguments)
    {
        Resolved.Add(code);
        return $"sentence for {code}";
    }
}
