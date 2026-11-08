using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Mappers;

/// <summary>
/// Restates a plan's <see cref="PhpLimits"/> in the three shapes it travels in: the wire a screen
/// reads and sends, the stored value, and the Sdk snapshot other modules read (issue #138).
/// </summary>
/// <remarks>
/// It translates and never decides (rules/csharp.md "A mapper translates; it never decides"): whether
/// a request states the bounds at all is the handler's question, and whether a figure is in range is
/// the validator's and the value's. Each method carries four figures across unchanged.
/// </remarks>
public static class PhpLimitsMapper
{
    /// <summary>Projects the bounds onto the wire.</summary>
    /// <param name="limits">The plan's bounds.</param>
    /// <returns>Their wire shape.</returns>
    public static PhpLimitsDto ToDto(PhpLimits limits)
    {
        return new PhpLimitsDto(limits.MemoryLimitMaxMb, limits.UploadMaxMb, limits.MaxExecutionSeconds, limits.MaxInputVars);
    }

    /// <summary>Reads validated wire bounds as the stored value.</summary>
    /// <param name="dto">Bounds that passed <see cref="Validators.PlanDefinitionValidator{TCommand}"/>.</param>
    /// <returns>The plan's bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a figure the validator would have refused.</exception>
    public static PhpLimits ToDomain(PhpLimitsDto dto)
    {
        return PhpLimits.Of(dto.MemoryLimitMaxMb, dto.UploadMaxMb, dto.MaxExecutionSeconds, dto.MaxInputVars);
    }

    /// <summary>Restates the bounds as the snapshot other modules read.</summary>
    /// <param name="limits">The plan's bounds.</param>
    /// <returns>The Sdk snapshot.</returns>
    public static PhpLimitsSnapshot ToSnapshot(PhpLimits limits)
    {
        return new PhpLimitsSnapshot(limits.MemoryLimitMaxMb, limits.UploadMaxMb, limits.MaxExecutionSeconds, limits.MaxInputVars);
    }
}
