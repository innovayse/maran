namespace Maran.Modules.Monitoring.Models;

/// <summary>Traffic across one bucket, in bytes per second in each direction.</summary>
/// <remarks>
/// A derived figure, never a stored one. It exists only as the output of
/// <see cref="Maran.Modules.Monitoring.Domain.Policies.NetworkRateCalculator"/>, which needs two readings and the time between them; a single
/// sample cannot produce one, which is why the first bucket of any chart has none at all rather than
/// a zero standing in for one.
/// </remarks>
/// <param name="ReceivedBytesPerSecond">Mean bytes received per second between this bucket's reading and the previous one.</param>
/// <param name="SentBytesPerSecond">Mean bytes sent per second over the same interval.</param>
public sealed record NetworkRate(double ReceivedBytesPerSecond, double SentBytesPerSecond);
