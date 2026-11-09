using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Domain.ValueObjects;

/// <summary>
/// What a plan allows a customer to set for PHP: four maxima, the plan's side of the customer's
/// php.ini settings (issue #138, plan decision D2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these four, and why together.</b> A customer may choose their own <c>memory_limit</c>,
/// upload size, execution time and input-variable count, but only up to what the plan sells. The four
/// are replaced whole, so a plan never holds one edit's memory bound beside another edit's upload
/// bound, which is why they are one value rather than four loose fields on the plan. The timezone has
/// no maximum and is not here.
/// </para>
/// <para>
/// <b>Every bound is positive and at or below the agent's wall.</b> The agent refuses a pool setting
/// above <see cref="PhpSettingCeilings"/> whatever a plan says, so a plan bound above it would sell
/// something the host never writes. <see cref="Of"/> refuses such a figure, so no plan can hold one.
/// </para>
/// <para>
/// <b>One source of the default.</b> <see cref="Default"/> is read from
/// <see cref="PhpLimitsSnapshot.Default"/>, the figures every consumer of the Sdk contract already
/// falls back to, so the plan's default and the snapshot's cannot drift apart.
/// </para>
/// </remarks>
public sealed record PhpLimits
{
    /// <summary>The largest <c>memory_limit</c> a customer may set, in mebibytes.</summary>
    public int MemoryLimitMaxMb { get; private set; }

    /// <summary>The largest <c>upload_max_filesize</c> and <c>post_max_size</c> a customer may set, in mebibytes.</summary>
    public int UploadMaxMb { get; private set; }

    /// <summary>The longest <c>max_execution_time</c> a customer may set, in seconds.</summary>
    public int MaxExecutionSeconds { get; private set; }

    /// <summary>The largest <c>max_input_vars</c> a customer may set.</summary>
    public int MaxInputVars { get; private set; }

    /// <summary>
    /// The conservative set a plan carries until its operator states one. A new instance on every
    /// read: EF Core persists the bounds as an owned value, and one instance reachable from two plans
    /// would be refused at the save as an owned entity shared between two owners.
    /// </summary>
    /// <value>The figures of <see cref="PhpLimitsSnapshot.Default"/>, each read its own instance.</value>
    public static PhpLimits Default
    {
        get
        {
            var snapshot = PhpLimitsSnapshot.Default;

            return Of(snapshot.MemoryLimitMaxMb, snapshot.UploadMaxMb, snapshot.MaxExecutionSeconds, snapshot.MaxInputVars);
        }
    }

    /// <summary>Builds the bounds; reached only through <see cref="Of"/>.</summary>
    /// <param name="memoryLimitMaxMb">The memory bound.</param>
    /// <param name="uploadMaxMb">The upload and post-size bound.</param>
    /// <param name="maxExecutionSeconds">The execution-time bound.</param>
    /// <param name="maxInputVars">The input-variable bound.</param>
    private PhpLimits(int memoryLimitMaxMb, int uploadMaxMb, int maxExecutionSeconds, int maxInputVars)
    {
        MemoryLimitMaxMb = memoryLimitMaxMb;
        UploadMaxMb = uploadMaxMb;
        MaxExecutionSeconds = maxExecutionSeconds;
        MaxInputVars = maxInputVars;
    }

    /// <summary>Parameterless constructor required by EF Core materialization of the owned value.</summary>
    private PhpLimits()
    {
    }

    /// <summary>States the four bounds.</summary>
    /// <param name="memoryLimitMaxMb">The memory bound, in mebibytes; 1 to <see cref="PhpSettingCeilings.MaxSizeMb"/>.</param>
    /// <param name="uploadMaxMb">The upload and post-size bound, in mebibytes; 1 to <see cref="PhpSettingCeilings.MaxSizeMb"/>.</param>
    /// <param name="maxExecutionSeconds">The execution-time bound, in seconds; 1 to <see cref="PhpSettingCeilings.MaxExecutionSeconds"/>.</param>
    /// <param name="maxInputVars">The input-variable bound; 1 to <see cref="PhpSettingCeilings.MaxInputVars"/>.</param>
    /// <returns>The bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a bound is below one or above the agent's ceiling.</exception>
    public static PhpLimits Of(int memoryLimitMaxMb, int uploadMaxMb, int maxExecutionSeconds, int maxInputVars)
    {
        Guard(memoryLimitMaxMb, PhpSettingCeilings.MaxSizeMb, nameof(memoryLimitMaxMb));
        Guard(uploadMaxMb, PhpSettingCeilings.MaxSizeMb, nameof(uploadMaxMb));
        Guard(maxExecutionSeconds, PhpSettingCeilings.MaxExecutionSeconds, nameof(maxExecutionSeconds));
        Guard(maxInputVars, PhpSettingCeilings.MaxInputVars, nameof(maxInputVars));

        return new PhpLimits(memoryLimitMaxMb, uploadMaxMb, maxExecutionSeconds, maxInputVars);
    }

    /// <summary>Refuses a bound below one or above its ceiling.</summary>
    /// <param name="value">The bound to check.</param>
    /// <param name="ceiling">The agent's ceiling for this setting.</param>
    /// <param name="parameterName">The parameter the bound came from.</param>
    private static void Guard(int value, int ceiling, string parameterName)
    {
        if (value < 1 || value > ceiling)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, value, $"A PHP bound must be from 1 to the agent's ceiling of {ceiling}.");
        }
    }
}
