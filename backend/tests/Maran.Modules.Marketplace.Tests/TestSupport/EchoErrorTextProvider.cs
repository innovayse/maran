using System.Diagnostics.CodeAnalysis;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>An error-text provider that answers every code with <c>text:</c> and the code, so a test can see which code a sentence was resolved from.</summary>
public sealed class EchoErrorTextProvider : IErrorTextProvider
{
    /// <summary>The prefix every resolved sentence carries.</summary>
    public const string Prefix = "text:";

    /// <inheritdoc />
    public string Resolve(string code, params object[] arguments)
    {
        return Prefix + code;
    }

    /// <inheritdoc />
    public bool TryResolve(string code, [NotNullWhen(true)] out string? text)
    {
        text = Prefix + code;
        return true;
    }
}
