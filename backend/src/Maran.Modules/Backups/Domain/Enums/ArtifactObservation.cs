namespace Maran.Modules.Backups.Domain.Enums;

/// <summary>What the start-up reconciler learned about one abandoned backup's archive.</summary>
/// <remarks>
/// Three members rather than a nullable boolean, because the caller must not be able to read
/// "could not ask" as "not there": that conflation is what would turn the pass back into an
/// inference from silence. None of the members is a placeholder for "no answer yet" — a question
/// that could not be asked is its own, named, outcome.
/// </remarks>
public enum ArtifactObservation
{
    /// <summary>The destination holds a readable archive under the row's id.</summary>
    Present,

    /// <summary>The destination was asked and holds no readable archive under the row's id.</summary>
    Absent,

    /// <summary>The question could not be asked at all: no account, no destination, or the agent failed.</summary>
    Unobservable,
}
