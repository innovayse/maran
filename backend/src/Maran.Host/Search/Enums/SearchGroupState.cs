namespace Maran.Host.Search.Enums;

/// <summary>
/// Whether one provider's group of hits could be produced. A state of its own rather than an empty list
/// for "could not be read": an empty list is a real answer (nothing matched), and a module that failed
/// or ran out of time is not a module that holds nothing. Serialized as its camelCase member name.
/// </summary>
public enum SearchGroupState
{
    /// <summary>The provider answered; the group carries its hits, possibly none.</summary>
    Ready,

    /// <summary>The provider failed or exceeded its time budget; the group carries no hits.</summary>
    Unavailable,
}
