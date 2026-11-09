using System.Diagnostics.CodeAnalysis;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Notifications.Tests.TestSupport;

/// <summary>An <see cref="IErrorTextProvider"/> that knows exactly the codes it is given.</summary>
public sealed class StubErrorTexts : IErrorTextProvider
{
    /// <summary>The sentence for each known code.</summary>
    private readonly Dictionary<string, string> _sentences;

    /// <summary>Creates the provider.</summary>
    /// <param name="sentences">Code to sentence.</param>
    public StubErrorTexts(Dictionary<string, string> sentences)
    {
        _sentences = sentences;
    }

    /// <inheritdoc />
    public string Resolve(string code, params object[] arguments)
    {
        return _sentences.TryGetValue(code, out var text) ? text : code;
    }

    /// <inheritdoc />
    public bool TryResolve(string code, [NotNullWhen(true)] out string? text)
    {
        return _sentences.TryGetValue(code, out text);
    }
}
