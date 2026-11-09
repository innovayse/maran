using System.Diagnostics.CodeAnalysis;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Files.Tests.TestSupport;

/// <summary>An error text provider whose sentence for a code is the code with a marker, so a test can see it was asked.</summary>
public sealed class StubErrorTextProvider : IErrorTextProvider
{
    /// <inheritdoc />
    public string Resolve(string code, params object[] arguments)
    {
        return "text:" + code;
    }

    /// <inheritdoc />
    public bool TryResolve(string code, [NotNullWhen(true)] out string? text)
    {
        text = "text:" + code;
        return true;
    }
}
