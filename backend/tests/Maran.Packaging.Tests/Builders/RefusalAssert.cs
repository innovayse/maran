using Maran.SharedKernel.Results;

namespace Maran.Packaging.Tests.Builders;

/// <summary>Assertions about a refusal, so every test states the code it expects and reads the same.</summary>
public static class RefusalAssert
{
    /// <summary>Asserts a result failed with a given code.</summary>
    /// <param name="code">The expected error code.</param>
    /// <param name="error">The error of the result under test; null when it succeeded.</param>
    public static void Is(string code, Error? error)
    {
        Assert.NotNull(error);
        Assert.Equal(code, error.Code);
    }
}
