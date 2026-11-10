using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Domain.ValueObjects;

namespace Maran.Modules.Licensing.Models;

/// <summary>
/// Everything the licence window needs to answer for any module: the verified licence status and the
/// standing of its lease, both judged at one instant. Read once and shared by every module question
/// until it is invalidated or ages out, so a request for the catalogue costs one read, not one per module.
/// It holds no licence text, signature or machine id.
/// </summary>
/// <param name="Status">The licence's verified status.</param>
/// <param name="Lease">The standing of its lease (<see cref="LeaseStanding.Missing"/> when there is no valid licence to lease).</param>
public sealed record LicenceCoverage(LicenceStatus Status, LeaseStanding Lease);
