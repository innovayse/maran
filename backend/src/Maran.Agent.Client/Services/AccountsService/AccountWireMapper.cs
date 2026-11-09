using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>
/// States the panel's account options in the wire's vocabulary: a slice ceiling or a figure as a
/// two-case <see cref="ResourceCeiling"/>, a login shell as a real <see cref="LoginShell"/>. It sits
/// beside <see cref="AgentAccountsClient"/> because the generated wire types may not leave this
/// project (rules/csharp.md "Canonical backend layout"), and it only restates: whether a figure is
/// allowed is decided by the caller, and what is outside the wire's range is refused here by an
/// exception instead of being wrapped into a different ceiling.
/// </summary>
internal static class AccountWireMapper
{
    /// <summary>States one slice ceiling in the wire's two-case shape.</summary>
    /// <param name="ceiling">The ceiling to send.</param>
    /// <returns>The wire ceiling, with exactly one of its cases set.</returns>
    internal static ResourceCeiling ToWire(SliceCeiling ceiling)
    {
        return ceiling.Kind switch
        {
            SliceCeilingKind.Limited => new ResourceCeiling { Maximum = ceiling.Maximum },
            SliceCeilingKind.Unlimited => new ResourceCeiling { Unlimited = new ResourceCeilingUnlimited() },
            _ => throw new ArgumentOutOfRangeException(nameof(ceiling), ceiling.Kind, "Unknown slice ceiling kind."),
        };
    }

    /// <summary>States a login shell in the wire's vocabulary.</summary>
    /// <param name="shell">The shell to send.</param>
    /// <returns>The wire shell, never the contract's zero value.</returns>
    internal static LoginShell ToWire(AccountLoginShell shell)
    {
        return shell switch
        {
            AccountLoginShell.Nologin => LoginShell.Nologin,
            AccountLoginShell.Bash => LoginShell.Bash,
            _ => throw new ArgumentOutOfRangeException(nameof(shell), shell, "Unknown login shell."),
        };
    }

    /// <summary>States a positive figure as a limited wire ceiling.</summary>
    /// <param name="figure">The ceiling; neither zero nor above what the wire's <c>uint32</c> carries.</param>
    /// <param name="parameterName">The caller's parameter, named in the exception.</param>
    /// <returns>The wire ceiling with its <c>maximum</c> case set.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for zero, which the agent refuses because an absent wire figure decodes to it, and for a
    /// figure the wire cannot carry, which would otherwise wrap into a different ceiling.
    /// </exception>
    internal static ResourceCeiling ToWireCeiling(ulong figure, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfZero(figure, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(figure, uint.MaxValue, parameterName);

        return new ResourceCeiling { Maximum = (uint)figure };
    }
}
