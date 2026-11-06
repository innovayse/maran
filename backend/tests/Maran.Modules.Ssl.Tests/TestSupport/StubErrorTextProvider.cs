using System.Diagnostics.CodeAnalysis;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="IErrorTextProvider"/> double that claims every code and answers it with a sentence
/// built from the code, so a test can see that a stored failure was resolved into a sentence.
/// </summary>
public sealed class StubErrorTextProvider : IErrorTextProvider
{
    /// <summary>The prefix every stub sentence starts with.</summary>
    public const string SentencePrefix = "Stub failure sentence for ";

    /// <inheritdoc />
    public string Resolve(string code, params object[] arguments)
    {
        return SentencePrefix + code;
    }

    /// <inheritdoc />
    public bool TryResolve(string code, [NotNullWhen(true)] out string? text)
    {
        text = SentencePrefix + code;
        return true;
    }
}
