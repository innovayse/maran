namespace Maran.Sdk.Contracts;

/// <summary>
/// One reading of the host as the live dashboard draws it: the instant it was taken and the four
/// parts the panel can already measure.
/// </summary>
/// <remarks>
/// Carries no text, so there is no string to cap or sanitise; every member is a number the agent
/// reported or a rate derived from two of them. A member the source could not measure is
/// <c>null</c> and never zero (rules/architecture.md "The backend owns the data"): a stored sample
/// rebuilt for the back-fill has no five- or fifteen-minute load, and a first reading has no rate.
/// </remarks>
/// <param name="At">When the reading was taken, from the panel's clock.</param>
/// <param name="Cpu">Processor utilisation and load.</param>
/// <param name="Memory">Memory in use and installed.</param>
/// <param name="Disk">The root filesystem.</param>
/// <param name="Network">Traffic rates.</param>
public sealed record LiveMetricsReading(
    DateTimeOffset At,
    LiveCpuReading Cpu,
    LiveMemoryReading Memory,
    LiveDiskReading Disk,
    LiveNetworkReading Network);
