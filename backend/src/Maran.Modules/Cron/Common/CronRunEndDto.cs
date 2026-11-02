using System.Text.Json.Serialization;

namespace Maran.Modules.Cron.Common;

/// <summary>
/// How a run started by hand ended, as the history shows it. Exactly one of three shapes, told apart
/// by <c>kind</c>, so a status never sits beside a ceiling that stopped the run and a screen never
/// has to read "no status" off an absent number.
/// </summary>
/// <remarks>
/// The constructor is private to this assembly, so <see cref="CronRunExitedDto"/>,
/// <see cref="CronRunWithoutStatusDto"/> and <see cref="CronRunStoppedDto"/> are the whole set.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CronRunExitedDto), "exited")]
[JsonDerivedType(typeof(CronRunWithoutStatusDto), "withoutStatus")]
[JsonDerivedType(typeof(CronRunStoppedDto), "stopped")]
public abstract record CronRunEndDto
{
    /// <summary>Closes the hierarchy to the three shapes this assembly declares.</summary>
    private protected CronRunEndDto()
    {
    }
}
