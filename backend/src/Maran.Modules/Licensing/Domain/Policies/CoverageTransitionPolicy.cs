using Maran.Modules.Licensing.Domain.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Domain.Policies;

/// <summary>
/// The ONE rule that says which changes of a module's licence cover are written to the audit journal:
/// the start of a lapse and the end of one. Pure, so the watcher that remembers the previous reading
/// and the journal that writes the entry share no judgement of their own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why only these two.</b> A lapse is the one change an administrator did not make: a licence or a
/// lease ran out. Installing a licence is already journalled by the install command, and a module that
/// was never covered becoming covered is that same install. A recovery is journalled because it closes
/// a lapse the trail has already shown.
/// </para>
/// <para>
/// <b>The first reading after the panel started has no previous.</b> The window is not persisted, so a
/// module found lapsed on the first reading is journalled as lapsed (the trail then says the panel found
/// it so after a start), and one found covered or not covered is not.
/// </para>
/// </remarks>
public static class CoverageTransitionPolicy
{
    /// <summary>Classifies the change from one reading to the next.</summary>
    /// <param name="previous">The verdict at the previous reading, or null when the module was not observed before.</param>
    /// <param name="current">The verdict now.</param>
    /// <returns>The transition to journal, or <see cref="CoverageTransition.None"/>.</returns>
    public static CoverageTransition Classify(EntitlementVerdict? previous, EntitlementVerdict current)
    {
        if (current == EntitlementVerdict.Lapsed && previous != EntitlementVerdict.Lapsed)
        {
            return CoverageTransition.Lapsed;
        }

        if (current == EntitlementVerdict.Covered && previous == EntitlementVerdict.Lapsed)
        {
            return CoverageTransition.Recovered;
        }

        return CoverageTransition.None;
    }
}
